namespace Sharpino

open FsToolkit.ErrorHandling
open Npgsql.FSharp
open FSharpPlus
open Sharpino
open Sharpino.Storage
open log4net
open log4net.Config

module KafkaPublisher =
    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    let inline updateToKafka<'A 
        when 'A:(static member StorageName: string)
        and 'A: (static member Version: string)
        > 
        (x: 'A) =
            let topic = 'A.StorageName + "-" + 'A.Version |> String.replace "_" ""
    
            ()

        