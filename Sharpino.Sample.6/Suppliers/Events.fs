namespace Tonyx.Sharpino.Pub

open Sharpino.Definitions
open Sharpino.Core
open Sharpino.Utils
open Tonyx.Sharpino.Pub.Supplier

module SupplierEvents =
    type SupplierEvents =
        | PhoneChanged of string
        | EmailChanged of string
            interface Event<Supplier> with
                member this.Process (supplier: Supplier) =
                    match this with
                    | PhoneChanged phone  ->
                        supplier.ChangePhone phone
                    | EmailChanged email ->
                        supplier.ChangeEmail email
        static member Deserialize (serializer: ISerializer, json: Json): Result<SupplierEvents, string>  =
            serializer.Deserialize<SupplierEvents> json    
        member this.Serialize (serializer: ISerializer) =   
            this
            |> serializer.Serialize
