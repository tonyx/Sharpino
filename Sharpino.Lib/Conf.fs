namespace Sharpino
open Microsoft.Extensions.Configuration
open log4net
open FSharp.Data
open Newtonsoft.Json
open System

module Conf =
    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    type Serialization = JsonSer | BinarySer // for future use
    // this will go away and the difference between test and prod db will be handled differently
    let isTestEnv = true

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
        }

    let defaultConf = 
        {
            LockType = LockType.Optimistic
            RefreshTimeout = 100
            CacheAggregateSize = 100
            PgSqlJsonFormat = PgSqlJson.PgJson
        }

    let config () =
        try
            let path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appSettings.json")    
            let json = System.IO.File.ReadAllText(path)
            let sharpinoConfig = JsonConvert.DeserializeObject<SharpinoConfig>(json)
            sharpinoConfig
        with
        | _ as ex ->
            log.Error("Error reading configuration", ex)
            log.Error("consider using or editing this configuration in your appSettings.json:")
            log.Error(JsonConvert.SerializeObject(defaultConf))
            // printf "%A\n" (JsonConvert.SerializeObject(defaultConf))
            defaultConf

        
    

