namespace Sharpino
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Configuration.Json
open log4net
open System

module Conf =
    let log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)
    type Serialization = JsonSer | BinarySer // for future use
    // this will go away and the difference between test and prod db will be handled differently
    let isTestEnv = true

    type SharpinoConfig =
        {
            PessimisticLock: bool
            RefreshTimeout: int
            CacheAggregateSize: int
        }

    let defaultConf = 
        { 
            PessimisticLock = false 
            RefreshTimeout = 100
            CacheAggregateSize = 100
        }

    let config () =
        let configuration = 
            ConfigurationBuilder().SetBasePath(AppDomain.CurrentDomain.BaseDirectory).AddJsonFile("appSettings.json").Build()
        let sharpinoConfig = configuration.GetSection("SharpinoConfig").Get<SharpinoConfig>()
        sharpinoConfig

