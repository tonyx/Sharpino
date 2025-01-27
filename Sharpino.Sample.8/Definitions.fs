namespace  Sharpino.TransportTycoon
open System

module Definitions =
    type TransportId = Guid
    type TruckId = Guid
    type SiteId = Guid
    type MayBeTransportTruck = Option<TruckId>
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
        EndNode: SiteId
        ConnectionType: ConnectionType
        TimeToTravel: int
    }
    with
        static member MkConnection
            (endNode: SiteId) (connectionType: ConnectionType) (timeToTravel: int) =
                {
                    EndNode = endNode
                    ConnectionType = connectionType
                    TimeToTravel = timeToTravel
                }
