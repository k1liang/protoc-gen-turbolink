dotnet publish protoc-gen-turbolink.csproj -c Release  -r linux-x64   --self-contained true   -o publish/linux
copy publish\linux\protoc-gen-turbolink ..\..\Plugins\TongosGrpc\GrpcLibraries\Linux /Y