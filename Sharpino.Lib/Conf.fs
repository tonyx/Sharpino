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
        
    type SharpinoConfig =
        {
            LockType: LockType
            RefreshTimeout: int
            CacheAggregateSize: int
        }

    let defaultConf = 
        {
            LockType = LockType.Optimistic
            RefreshTimeout = 100
            CacheAggregateSize = 100
        }

    let config () =
        let path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appSettings.json")    
        let json = System.IO.File.ReadAllText(path)
        let sharpinoConfig = JsonConvert.DeserializeObject<SharpinoConfig>(json)
        sharpinoConfig
        
    

