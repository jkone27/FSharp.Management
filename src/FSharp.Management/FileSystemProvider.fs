/// originally from https://gist.github.com/1241061
module internal FSharp.Management.FilesTypeProvider

open Samples.FSharp.ProvidedTypes
open FSharp.Management.Helper
open System.IO

// This is used to create proper relative paths.  Unfortunately, there is no managed API for it
let FILE_ATTRIBUTE_DIRECTORY = 0x10
let FILE_ATTRIBUTE_NORMAL = 0x80
let MAX_PATH_LENGTH = 260
[<System.Runtime.InteropServices.DllImport("shlwapi.dll", SetLastError = true)>]
extern int PathRelativePathTo(System.Text.StringBuilder pszPath, string pszFrom, int dwAttrFrom, string pszTo, int dwAttrTo)

// Determine whether a token is a file or directory
let GetPathAttribute path =
    if Directory.Exists(path) then FILE_ATTRIBUTE_DIRECTORY
    else if File.Exists(path) then FILE_ATTRIBUTE_NORMAL
    else failwith "Could not find specified file or path"


let GetRelativePath fromPath toPath =
    let fromAttr = GetPathAttribute fromPath
    let toAttr = GetPathAttribute toPath

    let path = System.Text.StringBuilder(MAX_PATH_LENGTH)
    let test = PathRelativePathTo(path, fromPath, fromAttr, toPath,toAttr)
    if test = 0 then raise(System.ArgumentException(sprintf "Paths [%s] and [%s] must have a root path" fromPath toPath))
    path.ToString()
    

let createFileProperties (dir:DirectoryInfo,dirNodeType:ProvidedTypeDefinition,relative) =
    try
        for file in dir.EnumerateFiles() do
            try
                let path = 
                    match relative with
                    | Some sourcePath -> GetRelativePath sourcePath file.FullName
                    | None -> file.FullName
                
                let pathField = ProvidedLiteralField(file.Name,typeof<string>,path)
                pathField.AddXmlDoc(sprintf "Path to '%s'" path)
                dirNodeType.AddMember pathField
            with
            | exn -> ()
    with
    | exn -> ()

let rec annotateDirectoryNode (ownerType:ProvidedTypeDefinition,dir:DirectoryInfo,propertyName,withParent,relative) =
    ownerType.HideObjectMethods <- true
    ownerType.AddXmlDoc(sprintf "A strongly typed interface to '%s'" dir.FullName)

    let path = 
        match relative with
        | Some sourcePath -> GetRelativePath sourcePath dir.FullName
        | None -> dir.FullName

    let pathField = ProvidedLiteralField("Path",typeof<string>,path)
    pathField.AddXmlDoc(sprintf "Path to '%s'" path)
    ownerType.AddMember pathField

    createFileProperties(dir,ownerType,relative)
    let typeSet = System.Collections.Generic.HashSet()
    
    if withParent && dir.Parent <> null then
        try
            let path =
                match relative with
                | Some sourcePath -> Some(sourcePath + "../")
                | None -> None
            ownerType.AddMember (createDirectoryNode(typeSet,dir.Parent,"Parent",withParent,path) ())
        with
        | exn -> ()

    try
        for subDir in dir.EnumerateDirectories() do
            try
                ownerType.AddMemberDelayed (createDirectoryNode(typeSet,subDir,subDir.Name,false,relative))
            with
            | exn -> ()
    with
    | exn -> ()

    ownerType 

and createDirectoryNode (typeSet,dir:DirectoryInfo,propertyName,withParent,relative) () =
    annotateDirectoryNode (nestedType<obj> typeSet propertyName,dir,propertyName,withParent,relative)

let createRelativePathSystem(resolutionFolder: string) =
    let dir = new DirectoryInfo(resolutionFolder)
    annotateDirectoryNode (erasedType<obj> thisAssembly rootNamespace "RelativePath",dir,dir.FullName,true,Some resolutionFolder)

let createTypedFileSystem() =  
    let typedFileSystem = erasedType<obj> thisAssembly rootNamespace "FileSystem"

    typedFileSystem.DefineStaticParameters(
        parameters = [ ProvidedStaticParameter("path", typeof<string>); ProvidedStaticParameter("relativeTo", typeof<string>, "")], 
        instantiationFunction = (fun typeName parameterValues ->
            match parameterValues with
            | [| :? string as path; :? string as relativePath |] -> 
                let dir = new DirectoryInfo(path)
                let relative = 
                    match relativePath with
                    | "" -> None
                    | _ -> Some relativePath
                annotateDirectoryNode (erasedType<obj> thisAssembly rootNamespace typeName,dir,dir.FullName,false,relative)
            | _ -> failwith "Wrong static parameters to type provider"))
            
    typedFileSystem
