# IL2FF

## What is IL2FF
IL2FF is a .NET to LittleBigPlanet (FF) transpiler.

## Project structure

* Enums: Some convenience enums from the game
* FishLib: Library to read and write script files
* IL2FF: The actual transpiler and library generator
* Native:  Classes and attributes used in scripts
* Samples: Some samples to demonstrate IL2FF capabilities

## Build
This project depends on [CwLibNet](https://github.com/Manoplay/CwLibNet), which must be added to the project through the integrated Submodule, this means, if you clone the project without `--recurse-submodules`, you should execute the following:
```bash
git submodule init
git submodule update
```

## Usage
Being a dotnet project, it can be invoked:
- using dotnet cli: `dotnet run --project IL2FF -- <args>`
- using the built executable file: `il2ff.exe <args>`

In this document, the second form will be used.

### Generating the game library
This step is mandatory and must be performed at least once.

Locate your game files, particularly `data.sdat` and `output/blurayguids.map`. The first one must be first decrypted using RPCS3. The decrypted file will now be referred to as the FARC, while the blurayguids as the MAP.

After you have both the FARC and the MAP ready, open a terminal window in the directory where you want your library to be located.

The command below can now be executed:
`il2ff.exe genlib <path-to-map> <path-to-farc>`

The library is now built and it's in the file `LittleBigPlanet.dll`

### Creating a script
You need to create an assembly with at least one class annotated with `Entrypoint`.
For example:
```csharp
using LittleBigPlanet;

[Entrypoint(revision = 626, branchID = 19524, branchRevision = 23)]
public class Sample: Thing {}
```

### Compiling an assembly
```
il2ff.exe compile <path-to-assembly> [output-directory]
```

## Credits
I almost forgot that section in the first commit of the project. There are some people I really wish to thank for the existence of the project.
* ennuo - thanks for the magical Toolkit
* Beyley - most of the code is inspired by ZishZingers
* Apple - most of the FishLib project (the library for handling FishFingers files) is inspired to a his private project and the idea of working with Dotnet would never have existed without him. And yes, many functions are just theirs.
* Disko - for providing base code for CwLibNet.

So a big **Thank you** is for the people mentioned here! Without you, the project would have never existed.
