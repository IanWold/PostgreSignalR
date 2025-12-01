<div align="center">
  <h1>PostgreSignalR</h1>

  Experimental, in-dev Postgres backplane for SignalR

  <a href="https://www.nuget.org/packages/PostgreSignalR"><img alt="NuGet Version" src="https://img.shields.io/nuget/vpre/PostgreSignalR?style=for-the-badge&logo=nuget"></a>
</div>

---

PostgreSignalR is an attempt to create a backplane for SingalR using Postgres. What the heck does that mean? [SignalR]() is an ASP.NET library for developing websocket applications, allowing bidirectional client and server communication. This is especially useful for applications where I want to send real-time notifications from my server to my clients. Websocket applications have an inherent horizontal scaling problem: if there are two server replicas with clients connected to each server, how would a client connected to Server A be able to be notified about an event originating on Server B?

SignalR introduces a _backplane_ concept to solve this problem: a _single_ Redis instance that multiple SignalR servers connect to, allowing SignalR to route internal messages between distributed servers. This way when Server A sends a websocket message, SignalR can notify its peers across Redis, allowing Server B to deliver the same message to its clients.

This is great, but what if [I really like Postgres](https://ian.wold.guru/Posts/just_use_postgresql.html) and want to use that instead of Redis? Postgres has [pub/sub functionality](https://www.postgresql.org/docs/current/sql-notify.html) not dissimilar to Redis, so it should be able to be used. While Microsoft only maintains an official backplane for Redis, it does expose the interfaces I need to implement in order to create a backplane using Postgres. This repository is an attempt to do just that; in fact, [the Redis backplane is open source under MIT](https://github.com/dotnet/aspnetcore/tree/main/src/SignalR/server/StackExchangeRedis), so this repository is built entirely off that codebase. The ASP MIT license has been copied to this repo.
