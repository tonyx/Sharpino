
namespace Sharpino.Sample.Entities
open FSharpPlus
open System
open Sharpino.Utils
open Sharpino.Core
open Sharpino.Sample.Todos.TodoEvents
open FsToolkit.ErrorHandling

module TodosReport =
    type TodosEvents =
        {
            InitTime: DateTime
            EndTime: DateTime
            TodoEvents: List<TodoEvent>
        }
        member 
            this.AddEvent (e: TodoEvent) =
                {
                    this with
                        TodoEvents = e :: this.TodoEvents
                }
        member
            this.AddEvents (es: List<TodoEvent>) =
                {
                    this with
                        TodoEvents = es @ this.TodoEvents
                }
    
