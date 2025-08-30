echo remember that rabbit must be active: rabbitmq-server
cd Sharpino.Sample.3.Test
dotnet run --configuration:rabbitmq
cd ..
cd Sharpino.Sample.6.Test
dotnet run --configuration:rabbitmq
cd ..
cd Sharpino.Sample.7/shoppingCartWithSharpino
dotnet run --configuration:rabbitmq
cd ../..
cd Sharpino.Sample.7/shoppingCartWithSharpinoBinary
dotnet run --configuration:rabbitmq
cd ../..
cd Sharpino.Sample.FormerlySaga.Test
dotnet run --configuration:rabbitmq
cd ..
cd Sharpino.Sample.8
dotnet run --configuration:rabbitmq
cd ..
cd Sharpino.Sample.9
dotnet run --configuration:rabbitmq
cd ..
cd Sharpino.Sample.10
dotnet run --configuration:rabbitmq
cd ..

