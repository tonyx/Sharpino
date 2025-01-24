namespace  Sharpino.TransportTycoon
open System

module Definitions =
    type SiteType =
        | Factory
        | Port
        | Destination of string

    type ConnectionType = Road | Sea
    
    type Connection = {
        EndNode: Guid
        ConnectionType: ConnectionType
        TimeToTravel: int
    }
    with
        static member MkConnection
            (endNode: Guid) (connectionType: ConnectionType) (timeToTravel: int) =
                {
                    EndNode = endNode
                    ConnectionType = connectionType
                    TimeToTravel = timeToTravel
                }
        
