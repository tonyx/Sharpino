namespace Sharpino.Sample._11

open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open Microsoft.Extensions.Logging
open Sharpino.CommandHandler
open Sharpino.Core
open Sharpino.Definitions

open Sharpino.EventBroker
open Sharpino.Sample._11.Student2
open Sharpino.Sample._11.StudentEvents2
open Sharpino.Sample._11.StudentCommands2
open Sharpino.Storage
open Sharpino
open System

module StudentManager2 =
    type StudentManager2
        (
            eventStore: IEventStore<string>,
            studentViewer: AggregateViewer<Student2>,
            messageSenders: MessageSenders,
            allStudentsAggregateStatesViewer: unit -> Result<(EventId * Student2) list, string>
        )
        =
        member this.AddStudent (student: Student2) =
            result
                {
                    return!
                        runInit<Student2, StudentEvents2, string>
                        eventStore
                        messageSenders
                        student
                }
        
        member this.AddMultipleStudents (students: Student2[]) =
            result
                {
                    return!
                        runMultipleInit<Student2, StudentEvents2, string>
                        eventStore
                        messageSenders
                        students
                }
        
        member this.GetStudent (id: Guid)  =
            result
                {
                    let! _, student = studentViewer id
                    return student
                }
        
        member this.AddAnnotationToStudent (id: Guid, note: string) =
            result
                {
                    let addNoteToStudent = AddAnnotation note
                    return!
                        runAggregateCommand<Student2, StudentEvents2, string>
                        id
                        eventStore
                        messageSenders
                        addNoteToStudent
                }
        
        member this.GetAllStudents () =
            result
                {
                    let! students = allStudentsAggregateStatesViewer()
                    return (students |>> snd)
                }        