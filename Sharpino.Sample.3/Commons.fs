module Sharpino.Sample._3.Commons

open System

type SeatId =
    private SeatId of Guid
    with
        static member New = SeatId(Guid.NewGuid())
        member this.Id =
            this |> fun (SeatId id) -> id

            