namespace Sharpino
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions
open log4net
open FSharp.Data
open Newtonsoft.Json
open System

module Conf =
    // let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    let logger: ILogger ref = ref NullLogger.Instance
    let setLogger (newLogger: ILogger) =
        logger := newLogger

    type Serialization = JsonSer | BinarySer // for future use
    // this will go away and the difference between test and prod db will be handled differently
    let isTestEnv = true

    [<Obsolete "will be always optimistic and this conf will be removed">]
    type LockType =
        | Pessimistic
        | Optimistic

    type PgSqlJson =
        | PlainText     
        | PgJson
    type SharpinoConfig =
        {
            LockType: LockType
            RefreshTimeout: int
            CacheAggregateSize: int
            PgSqlJsonFormat: PgSqlJson
            MailBoxCommandProcessorsSize: int
            EventStoreTimeout: int
        }

    let defaultConf = 
        {
            LockType = LockType.Optimistic
            RefreshTimeout = 100
            CacheAggregateSize = 100
            PgSqlJsonFormat = PgSqlJson.PgJson
            MailBoxCommandProcessorsSize = 1000
            EventStoreTimeout = 1000 
        }

    let config () =
        try
            let path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sharpinoSettings.json")    
            let json = System.IO.File.ReadAllText(path)
            let sharpinoConfig = JsonConvert.DeserializeObject<SharpinoConfig>(json)
            sharpinoConfig
        with
        | _ as ex ->
            printf "there is no sharpinoSettings.json file, using default configuration\n"
            logger.Value.LogError("Error reading configuration", ex)
            // log.Error("Error reading configuration", ex)
            // log.Error("consider using or editing this configuration in your appSettings.json:")
            logger.Value.LogError("Consider using or editing this configuration in your appSettings.json:")
            // log.Error(JsonConvert.SerializeObject(defaultConf))
            logger.Value.LogError("Using default configuration")
            logger.Value.LogError(JsonConvert.SerializeObject(defaultConf))
            
            defaultConf

        
    

