namespace Shared

open System

type Todo = { Id: Guid; Description: string }

module Todo =
    let isValid (description: string) =
        String.IsNullOrWhiteSpace description |> not

    let create (description: string) = {
        Id = Guid.NewGuid()
        Description = description
    }

module Entities =

    type Id = int
    type SeatState =
        | Booked
        | Free
    type Seat =
        { Id: Id
          State: SeatState
          RowId: Option<System.Guid>
        }
    type Booking =
        { Id: Id
          SeatIds: List<Id>
        }
        with member
                this.isEmpty() =
                    this.SeatIds |> List.isEmpty

    type SeatsRowTO =
        {
            Seats: List<Seat>
            Id: Guid
        }

module Route =
    let builder typeName methodName =
        sprintf "/api/%s/%s" typeName methodName

type ITodosApi = {
    getTodos: unit -> Async<Todo list>
    addTodo: Todo -> Async<Todo>
}
module Services =
    open Entities
    type IRestStadiumBookingSystem =
        {
            AddRowReference : unit -> Async<Result<unit, string>>
            BookSeats : Guid * Booking -> Async<Result<unit, string>>
            BookSeatsNRows : List<Guid * Booking> -> Async<Result<unit, string>>
            // GetRowTO : Guid -> Async<Result<SeatsRowTO,string>>
            AddSeat: Guid * Seat -> Async<Result<unit, string>>
            RemoveSeat: Seat -> Async<Result<unit, string>>
            AddSeats: Guid * List<Seat> -> Async<Result<unit, string>>
            GetAllRowReferences: unit -> Async<Result<List<Guid>,string>>
            GetAllRowTOs: unit -> Async<Result<List<SeatsRowTO>, string>>
        }
