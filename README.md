<div align="center">
  <h1>PostgreSignalR</h1>

  Experimental, in-dev Postgres backplane for SignalR

  <a href="https://www.nuget.org/packages/PostgreSignalR"><img alt="NuGet Version" src="https://img.shields.io/nuget/vpre/PostgreSignalR?style=for-the-badge&logo=nuget"></a>
</div>

---

PostgreSignalR is an attempt to create a backplane for SingalR using Postgres. What the heck does that mean? [SignalR]() is an ASP.NET library for developing websocket applications, allowing bidirectional client and server communication. This is especially useful for applications where I want to send real-time notifications from my server to my clients. Websocket applications have an inherent horizontal scaling problem: if there are two server replicas with clients connected to each server, how would a client connected to Server A be able to be notified about an event originating on Server B?

SignalR introduces a _backplane_ concept to solve this problem: a _single_ Redis instance that multiple SignalR servers connect to, allowing SignalR to route internal messages between distributed servers. This way when Server A sends a websocket message, SignalR can notify its peers across Redis, allowing Server B to deliver the same message to its clients.

This is great, but what if [I really like Postgres](https://ian.wold.guru/Posts/just_use_postgresql.html) and want to use that instead of Redis? Postgres has [pub/sub functionality](https://www.postgresql.org/docs/current/sql-notify.html) not dissimilar to Redis, so it should be able to be used. While Microsoft only maintains an official backplane for Redis, it does expose the interfaces I need to implement in order to create a backplane using Postgres. This repository is an attempt to do just that; in fact, [the Redis backplane is open source under MIT](https://github.com/dotnet/aspnetcore/tree/main/src/SignalR/server/StackExchangeRedis), so this repository is built entirely off that codebase. The ASP MIT license has been copied to this repo.

# Getting Started

Setting up the Postgres backplane for SingalR is very simple. If you've configured the [official Redis backplane]() before these steps will be quite simple.

1. You'll need a Postgres server of course; deploy a new one or use your exisitng database
2. Install the [PostgreSignalR Nuget package](https://www.nuget.org/packages/PostgreSignalR) in your server project
3. In your ASP setup logic, add the postgres backplane to the service builder:

```csharp
builder.Services.AddSignalR().AddPostgresBackplane("<your_postgres_connection_string>");
```

4. Optionally, you can configure options for the backplane:

```csharp
builder.Services.AddSignalR().AddPostgresBackplane("<your_postgres_connection_string>", options =>
{
    // Prefix is added to the channel names that PostgreSignalR publishes in Postgres
    // If you are using one Postgres database for multiple SignalR apps, you should
    // use a different prefix for each app.
    options.Prefix = "myapp";
);
```

# Roadmap

This library is brand-new, so while it should support all SignalR features it hasn't been thoroughly tested and vetted for production use. The immediate next steps are aimed at making this production-ready:

1. Automated tests to ensure the functionality of the library
2. Performance tests and benchmarks to optimize the code and find at-scale bugs
3. Writing one or several example applications utilizing all of the SignalR features
4. Adding additional configuration options to allow more flexibility in error handling

Right now the library has been released on Nuget in an alpha version, denoting that it is relatively untested _and_ the API may be subject to change. In this case, the "API" consists entirely of the dependency injection extensions and the `PostgresOptions` class. The beta, rc, and prod milestones will denote:

* Beta: Testing has given confidence that this is a viable product and major bugs and performance issues have been resolved
* RC: Further changes to the interface are unlikely and testing has indicated that the product is stable
* Prod: The library is ready for use in production systems

# Contributing

Thank you for wanting to contribute! I'm very happy to accept any contributions. At the present moment the most required work is testing - either writing automated tests or manually testing on real SignalR applications. However, if you want to contribute in any other way that is also always welcome.

* ❓ **Questions** should be asked by opening a discussion. To help keep things tidy, please read through the existing discussions and issues first in case your question is already answered or can be asked within one of those spaces.
* 🛠️ **Issues** which are open and not assigned can be claimed - comment in the issue that you want to take it on and I'm happy to assign it to you! Be sure to mention if you ever need any assistance or clarification, I'll be able to help. If you take an issue on and it becomes apparent you're not going to be able to finish the work, that is OK too - just be sure to keep the issue informed about that development. If you take an issue on and go for a long period of time without checking in, I will reassign the issue.
* 🐞 **Bugs** can be reported by opening an issue directly. Before submitting the bug please be sure you can answer the four bug questions: "What did you do?", "What happened?", "What did you expect to happen instead?", "Why did you expect that to happen?". Provide as much detail in your bug report as you can. Bug reports which can't provide enough detail to replicate or that can't answer the four bug questions will be rejected.
* 🔬 **New Tests or Example Apps** can be submitted directly with a PR, though anticipate some conversation in the PR to ensure that the new code is fitting into the broader picture correctly. It's good to start with a PR though, as a conceptual conversation around a specific test (for example) is unlikely to yield a productive result without having tangible code to look at.
* 💡 **New Features or Ideas** should start by opening a discussion instead of an issue. If that conversation results in wanting to move forward with the idea, one or more issues will be created that can then be taken on.
