# Udon-Type-Exporter
Exports types and methods from Udon into a C# DLL

To install put `ExtendedUASM` and `TooDon Exporter` into `Assets\Udon\Editor`.

Once that's done, at the top a drop down called TooDon will appear. Click that and then click "export udon types". 

To use the DLL in Visual Studio for auto complete, remove all reference to other DLLs in your Visual Studio Proejct, then add `<NoStdLib>true</NoStdLib>` to the `.csproj` file for your project. Put that line inside one of the PropertyGroups that doesn't have a condition.