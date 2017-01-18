Dynamo
====

A proof-of-concept dynamic script-based plugin system in C#.

`*.cs` files in `.\plugins\` will be compiled to DLLs and be loaded into the running application.
Changes to the `*.cs` file will trigger a rebuild and re-import.
