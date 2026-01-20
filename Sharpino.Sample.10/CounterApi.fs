namespace Sharpino.Sample._10

open System.Threading.Tasks
open Sharpino
open Sharpino.Core
open Sharpino.EventBroker
open Sharpino.RabbitMq
open Sharpino.Sample._10.Models.Account
open Sharpino.Sample._10.Models.AccountCommands
open Sharpino.Sample._10.Models.AccountEvents
open Sharpino.Sample._10.Models.Counter
open Sharpino.Sample._10.Models.Counter
open Sharpino.Sample._10.Models.Counter
open Sharpino.CommandHandler
open Sharpino.Storage
open Sharpino.CommandHandler
open Sharpino.Sample._10.Models.Events
open Sharpino.Sample._10.Models.Commands

open Sharpino.CommandHandler
open Sharpino.Core
open Sharpino.Sample._10.Models
open Sharpino.Sample._10.Models.Counter
open Sharpino.Storage
open FSharpPlus.Operators
open FsToolkit.ErrorHandling
open System
    
module CounterApi =
    let emptyMessageSenders: StreamName -> MessageSender =
        fun _ ->
            fun _ ->
                ValueTask.CompletedTask                

type CounterApi
    (eventStore: IEventStore<byte[]>,
     messageSenders: MessageSenders,
     counterStateViewer: AggregateViewer<Counter>,
     accountStateViewer: AggregateViewer<Account>)
    =
    member this.CreateCounter (counter: Counter) =
        result
            {
                let! result = runInit<Counter, CounterEvents, byte[]> eventStore messageSenders counter
                return result
            }
    member this.GetCounter (id: Guid) =
        result
            {
                let! (_, result) = counterStateViewer id
                return result
            }
    member this.IncrementCounter (id: Guid) =
        result
            {
                let! counter = this.GetCounter id
                let! result = 
                    runAggregateCommand<Counter, CounterEvents, byte[]> id eventStore messageSenders Increment
                return result
            }
    member this.CreateAccount (account: Account) =
        result
            {
                let! result = runInit<Account, AccountEvents, byte[]> eventStore messageSenders account
                return result
            }
    member this.GetAccount (id: Guid) =
        result
            {
                let! (_, result) = accountStateViewer id
                return result
            }        
            
    member this.IncrementManyCounters (ids: List<Guid>) =
        result
            {
                return!
                    runNAggregateCommands<Counter, CounterEvents, byte[]> ids eventStore messageSenders ([0 .. ids.Length - 1] |>> (fun _ -> Increment))
            }
    
    member this.IncrementCountersAndAccounts (counterIds: List<Guid>, accountIds: List<Guid>) =
        result
            {
                let! preExecutedAggregateCommands =
                    counterIds
                    |> List.traverseResultM (fun id ->
                        preExecuteAggregateCommandMd<Counter, CounterEvents, byte[]> id eventStore messageSenders "md" Increment
                    )
                let! preExecuteAggregateCommands2 =
                    accountIds
                    |> List.traverseResultM (fun id ->
                        preExecuteAggregateCommandMd<Account, AccountEvents, byte[]> id eventStore messageSenders "md" (AddAmount 1)
                    )
                let totalPreExecutedAggregateCommands =
                    preExecutedAggregateCommands @ preExecuteAggregateCommands2
                    
                return!    
                    runPreExecutedAggregateCommands2<byte[]> totalPreExecutedAggregateCommands eventStore messageSenders
            }