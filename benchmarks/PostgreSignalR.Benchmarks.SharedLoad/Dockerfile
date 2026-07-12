FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the rest of the source
COPY benchmarks/ benchmarks/

# Restore just the shared-load project
RUN dotnet restore benchmarks/PostgreSignalR.Benchmarks.SharedLoad/PostgreSignalR.Benchmarks.SharedLoad.csproj

# Publish
RUN dotnet publish benchmarks/PostgreSignalR.Benchmarks.SharedLoad/PostgreSignalR.Benchmarks.SharedLoad.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app

COPY --from=build /out .
ENTRYPOINT ["dotnet", "PostgreSignalR.Benchmarks.SharedLoad.dll"]
