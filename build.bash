rd /s /q .\KappuCitti.Plugin.SyncVote\bin
rd /s /q .\KappuCitti.Plugin.SyncVote\obj
dotnet restore
dotnet build SyncVote.sln -c Release /property:GenerateFullPaths=true /consoleloggerparameters:NoSummary

