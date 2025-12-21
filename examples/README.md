# Examples

These example applications demonstrate different ways you can configure PostgreSignalR to suit your needs.

## [Basic Setup](https://github.com/IanWold/PostgreSignalR/tree/main/examples/PostgreSignalR.Examples.BasicSetup)

This project demonstrates the basic configuration options for PostgreSignalR, not considering any advanced setups below. The default options will be good for the majority of use cases, but some simple configurations may want to be chosen.

The philosophy this project uses for default configuration options are:

1. Defaults should cover the majority of cases,
2. They should be the simplest options, and
3. They should add the least amount of burden to the user.

The options demonstrated in this example are: `Prefix`, `ChannelNameNormalization`, and `OnInitialized`. See the comments in [Program.cs](https://github.com/IanWold/PostgreSignalR/blob/main/examples/PostgreSignalR.Examples.BasicSetup/Program.cs) for detailed explanations of each.

In order to run this example, you will need to provide your own Postgres connection string in [appsettings.json](https://github.com/IanWold/PostgreSignalR/blob/main/examples/PostgreSignalR.Examples.BasicSetup/appsettings.json).

## Table Payload Strategy



### Custom Table



## Custom Payload Strategy