﻿namespace EventSourcing

module Projection =
 
    /// the Projections's builder-type - includes bookkepping for intermediate types and should be hidden
    /// from (poor) users view
    type T<'e,'i,'a> = private { foldF : 'i -> 'e  -> 'i; project : 'i -> 'a; init : 'i }
 
    /// creates a projection, based on a fold over intermediate values and a final projection
    let createWithProjection (p : 'i -> 'a) (i : 'i) (f : 'i -> 'e -> 'i) =
        { foldF = f
          project = p
          init = i
        }
 
    /// create a projection based on simple fold
    let create (init : 'a) (f : 'a -> 'e -> 'a) =
        createWithProjection id init f

    let internal initValue (p : T<'e,'i,'a>) : 'i =
        p.init

    let internal step (p : T<'e,'i,'a>) (state : 'i) (ev : 'e) : 'i =
        p.foldF state ev
 
    let internal project (p : T<'e,'i,'a>) (state : 'i) : 'a =
        p.project state

    /// folds events using a projection (starting form init) into a final output
    let foldFrom (b : T<'e,'i,'a>) (init : 'i) : 'e seq -> 'a =
        Seq.fold b.foldF init >> b.project
 
    /// folds events using a projection into a final output
    let fold (b : T<'e,'i,'a>) : 'e seq -> 'a =
        foldFrom b (b.init)
 
 
    // those views are applicative - so let's implement the operations:

    /// maps the output of a projection using a function f
    let map (f : 'a -> 'b) (a : T<'e,'i,'a>) : T<'e,'i,'b> =
        { foldF = a.foldF
          project = a.project >> f
          init = a.init
        }
 
    /// applicative sequence - folds f and a over the events and applies the final projected value from a to the final projected value of f
    let sequence (f : T<'e,'i1,('a -> 'b)>) (a : T<'e,'i2,'a>) : T<'e, 'i1*'i2,'b> =
        { foldF = fun (i1,i2) e -> (f.foldF i1 e, a.foldF i2 e)
          project = fun (i1,i2) -> (f.project i1) (a.project i2)
          init = (f.init, a.init)
        }
 
    /// applicative pure - creates a projection that ignores the events and returns constant a
    let constant (a : 'a) : T<'e, unit,'a> =
        { foldF = fun () _ -> ()
          project = fun () -> a
          init = ()
        }

    // some simple projections

    /// maps / filters all events - it will map the events with f and
    //// choose all values `a` where `f event = Some a`
    let inline choose (f : 'e -> 'a option) : T<'e,_,'a list> =
        createWithProjection List.rev [] (fun acc ev -> match f ev with Some a -> a::acc | None -> acc)

    /// gets all events
    let events() : T<'e,_,'e list> =
        choose Some

    /// filters all events
    let filter (f : 'e -> bool) : T<'e,_,'e list> =
        choose (fun ev -> if f ev then Some ev else None)

    /// sums values - f chooses the summands from the events
    let inline sumBy (f : 'e -> ^a option) : T<'e,_,^a> =
        let zero : ^a = LanguagePrimitives.GenericZero
        create zero (fun sum e -> sum + match f e with Some n -> n | None -> zero)

    /// gets the latest result of type (Some v) that f returns for a event
    let inline latest (f : 'e -> 'a option) : T<'e,_,'a> =
        let init = Unchecked.defaultof<'a>
        create init (fun l e -> match f e with Some v -> v | None -> l)

    /// gets the sinlge result of type (Some v) that f returns for a event 
    /// and throws an exception if there is not exactly one such event
    let inline single (f : 'e -> 'a option) : T<'e,_,'a> =
        createWithProjection Option.get None (fun acc e -> 
            match (acc, f e) with 
            | (None, Some v)   -> Some v 
            | (Some _, Some _) -> failwith "more than one event yielding a value"
            | _                -> acc)

    let inline any (f : 'e -> bool) : T<'e,_,bool> =
        create false (fun acc e ->
            match acc with
            | true  -> true
            | false -> f e
            )

    let inline all (f : 'e -> bool) : T<'e,_,bool> =
        create true (fun acc e ->
            match acc with
            | false -> false
            | true  -> f e
            )


    let (<*>) = sequence
    let ($)   = map

    let inline pair (a : T<'e,'i1,'a>) (b : T<'e,'i2,'b>) : T<'e,'i1*'i2,'a*'b> =
        (fun a b -> (a,b)) $ a <*> b


[<AutoOpen>]
module ProjectionOperations =
    open Projection
    let (<*>) = sequence
    let ($)   = map
    let (<|>) = pair
