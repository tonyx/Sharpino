
namespace Sharpino.Sample.Entities
open FSharpPlus
open System
open Sharpino.Utils
open Sharpino.Core
open Sharpino.Sample.Todos.TodoEvents
open FsToolkit.ErrorHandling

module TodosReport =
    // make it only one with generic type
    type TodosEvents<'TEvent> =
        {
            InitTime: DateTime
            EndTime: DateTime
            TodoEvents: List<'TEvent>
        }
        member 
            this.AddEvent (e: 'TEvent) =
                {
                    this with
                        TodoEvents = e :: this.TodoEvents
                }
        member
            this.AddEvents (es: List<'TEvent>) =
                {
                    this with
                        TodoEvents = es @ this.TodoEvents
                }

    // type TodosEvents' =
    //     {
    //         InitTime: DateTime
    //         EndTime: DateTime
    //         TodoEvents: List<TodoEvent'>
    //     }
    //     member 
    //         this.AddEvent (e: TodoEvent') =
    //             {
    //                 this with
    //                     TodoEvents = e :: this.TodoEvents
    //             }
    //     member
    //         this.AddEvents (es: List<TodoEvent'>) =
    //             {
    //                 this with
    //                     TodoEvents = es @ this.TodoEvents
    //             }

    
