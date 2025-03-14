namespace  Sharpino.TransportTycoon
open System

module Definitions =
    type ConnectionId = Guid
    type TransporterId = Guid
    type SiteId = Guid
    type MayBeTransportTruck = Option<TransporterId>
    type DestinationCode = string
    
    type SiteType =
        | Factory
        | Port
        | Destination of DestinationCode

    type ConnectionType = Road | Sea
    type TransporterType =
        | TruckType 
        | ShipType of MayBeTransportTruck

    type Connection = {
        Id: ConnectionId
        InitialSitePath: SiteId
        DestinationSitePath: SiteId
        EndInterval: SiteId
        ConnectionType: ConnectionType
        TimeToTravel: int
    }
    with
        static member MkConnection
            (endInterval: SiteId) (initialSitePath: SiteId) (destinationSitePath: SiteId) (connectionType: ConnectionType) (timeToTravel: int) =
                {
                    Id = Guid.NewGuid()
                    InitialSitePath = initialSitePath
                    DestinationSitePath = destinationSitePath
                    EndInterval = endInterval // no need to specify the initial interval as it is the site where we attach this interval
                    ConnectionType = connectionType
                    TimeToTravel = timeToTravel
                }
