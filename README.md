# Udon-Type-Exporter
Exports types and methods from Udon into a C# DLL

Add `\TooDon Exporter` and `\ExtendedUASM` to `Udon\Assets\Udon\Editor`. 
Once Unity compiles, there will be a drop down at the top called "TooDon". 
Click TooDon->Export Udon Types.

In order to use this DLL in Visual Studio for auto completion, you must delete all references in the project, then add `<NoStdLib>true</NoStdLib>` to your `.csproj` file
