
namespace Sharpino

open System
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Configuration
open Sharpino
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions
open System.Runtime.CompilerServices
open System.Collections
open FSharp.Core

open System.Threading.Tasks
open FsToolkit.ErrorHandling

module MailBoxProcessors =
    let builder = Host.CreateApplicationBuilder()
    let config = builder.Configuration
    let loggerFactory = LoggerFactory.Create(fun b ->
        if config.GetValue<bool>("Logging:Console", true) then
            b.AddConsole() |> ignore
        )
    
    let logger = builder.Services.BuildServiceProvider().GetRequiredService<ILoggerFactory>().CreateLogger("Sharpino.MailBoxProcessors")
    
    [<Obsolete("This method is deprecated and will be removed in a future version. Please config log on appsettings.json")>]
    let setLogger (newLogger: ILogger) =
        ()

    type Command =
        | AsyncTaskCommand of (unit -> Task<Result<unit, string>>)
        | AsyncComputationCommand of (unit -> Async<Result<unit, string>>)
        | SyncCommand of (unit -> Result<unit, string>)

    type UnitResult = Command * AsyncReplyChannel<Result<unit, string>>
    
    type Processors private() =
        let processors = Concurrent.ConcurrentDictionary<string, MailboxProcessor<UnitResult>>()
        let idleTimeoutMs = config.GetValue<int>("MailBoxIdleTimeoutMs", 300_000)
        static let instance = Processors()
        static member Instance = instance

        member this.GetProcessor (name: string) =
            processors.GetOrAdd(name, fun key -> this.createProcessor key)

        member private this.createProcessor (name: string) =
            let executeCommand (cmd: Command, reply: AsyncReplyChannel<Result<unit, string>>) =
                async {
                    try
                        match cmd with
                        | AsyncTaskCommand f ->
                            let! result = f() |> Async.AwaitTask
                            reply.Reply result
                        | AsyncComputationCommand f ->
                            let! result = f()
                            reply.Reply result
                        | SyncCommand f ->
                            let result = f()
                            reply.Reply result
                    with ex ->
                        logger.LogError(sprintf "Exception executing command in MailboxProcessor %s: %A" name ex)
                        reply.Reply (Error ex.Message)
                }

            MailboxProcessor<UnitResult>.Start (fun inbox ->
                let rec loop () =
                    async {
                        let! msgOpt = inbox.TryReceive(idleTimeoutMs)
                        match msgOpt with
                        | Some msg ->
                            do! executeCommand msg
                            return! loop()
                        | None ->
                            let kvp = Generic.KeyValuePair<string, MailboxProcessor<UnitResult>>(name, inbox)
                            let removed = processors.TryRemove(kvp)
                            if removed then
                                let! finalCheck = inbox.TryReceive(0)
                                match finalCheck with
                                | Some msg ->
                                    processors.TryAdd(name, inbox) |> ignore
                                    do! executeCommand msg
                                    return! loop()
                                | None ->
                                    logger.LogDebug(sprintf "MailboxProcessor for %s timed out due to inactivity and was retired." name)
                                    ()
                            else
                                return! loop()
                    }
                loop()
            )
   
    let postToTheProcessorAsync (processor: MailboxProcessor<UnitResult>) (f: unit -> Task<Result<unit, string>>) : Task<Result<unit, string>> =
        // timeout is hardcoded here. next release will be a conf
        Async.StartAsTask (processor.PostAndAsyncReply ((fun reply -> (AsyncTaskCommand f, reply)), Commons.generalAsyncTimeOut))

    let postToTheProcessorComputationAsync (processor: MailboxProcessor<UnitResult>) (f: unit -> Async<Result<unit, string>>) : Async<Result<unit, string>> =
        processor.PostAndAsyncReply ((fun reply -> (AsyncComputationCommand f, reply)), Commons.generalAsyncTimeOut)

    let postToTheProcessorSync (processor: MailboxProcessor<UnitResult>) (f: unit -> Result<unit, string>) : Result<unit, string> =
        Async.RunSynchronously (processor.PostAndAsyncReply ((fun reply -> (SyncCommand f, reply)), Commons.generalAsyncTimeOut))

    type PostInvoker =
        static member inline Invoke (_: PostInvoker, processor: MailboxProcessor<UnitResult>, f: unit -> Task<Result<unit, string>>) : Task<Result<unit, string>> =
            postToTheProcessorAsync processor f
        static member inline Invoke (_: PostInvoker, processor: MailboxProcessor<UnitResult>, f: unit -> Async<Result<unit, string>>) : Async<Result<unit, string>> =
            postToTheProcessorComputationAsync processor f
        static member inline Invoke (_: PostInvoker, processor: MailboxProcessor<UnitResult>, f: unit -> Result<unit, string>) : Result<unit, string> =
            postToTheProcessorSync processor f

    let inline postToTheProcessor (processor: MailboxProcessor<UnitResult>) (f: 'F) =
        ((^Invoker or ^F) : (static member Invoke: ^Invoker * MailboxProcessor<UnitResult> * 'F -> 'Res) (Unchecked.defaultof<PostInvoker>, processor, f))
        
