using UnityEngine;
using UnityEditor;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Generic;
using System.IO;
using System;
using System.Reflection;
using System.Dynamic;
using ICSharpCode.SharpZipLib.Zip;
using Newtonsoft.Json;
using SimpleJSON;

public class CompilerTool : EditorWindow {
    static string ScriptPath = @"C:\open\fbsource\arvr\projects\pioneer\AugmentedCalling\AugmentedCalling\Assets\Script.cs";
    static string DllPath = @"C:\open\fbsource\arvr\projects\pioneer\AugmentedCalling\AugmentedCalling\Library\ScriptAssemblies\";
    string Result = "";
    string LastScript;
    static string LatestDllName; 

    static IEnumerable<string> Namespaces = new[] {
                "System",
                "System.IO",
                "System.Text",
                "System.Collections.Generic",
                "UnityEngine",
                "UnityEditor"
            };

    static IEnumerable<MetadataReference> References = new[] {
        MetadataReference.CreateFromFile(typeof(string).Module.Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Application).Module.Assembly.Location),
        MetadataReference.CreateFromFile(typeof(EditorGUI).Module.Assembly.Location),
        MetadataReference.CreateFromFile(typeof(GUI).Module.Assembly.Location),
        MetadataReference.CreateFromFile(typeof(GUILayout).Module.Assembly.Location),
        MetadataReference.CreateFromFile(typeof(System.Dynamic.CallInfo).Module.Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.CSharp.RuntimeBinder.RuntimeBinderException).Module.Assembly.Location),
        MetadataReference.CreateFromFile(typeof(CompilerTool).Module.Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ZipInputStream).Module.Assembly.Location),
        MetadataReference.CreateFromFile(typeof(JsonReader).Module.Assembly.Location),
        MetadataReference.CreateFromFile(typeof(StringReader).Module.Assembly.Location),
        MetadataReference.CreateFromFile(typeof(JSON).Module.Assembly.Location),
    };

    static CSharpCompilationOptions Options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        .WithOverflowChecks(true).WithOptimizationLevel(OptimizationLevel.Release)
        .WithUsings(Namespaces);

     
    //======================================================================================================================================================//
    [MenuItem("Window/Compiler Tool")]
    static void Init() {
        CompilerTool window = (CompilerTool)GetWindow(typeof(CompilerTool));
        window.Show();
    }

    //======================================================================================================================================================//
    void OnEnable() {
        UpdateDll();
        LastScript = "";
    }

    //======================================================================================================================================================//
    void Update() {
        Repaint();
    }

    //======================================================================================================================================================//
    void OnGUI() {
        Compile();
        Run();
        EditorGUILayout.TextArea(DllFullPath());        
        EditorGUILayout.TextArea(Result);
        EditorPrefs.SetBool("kAutoRefresh", GUILayout.Toggle(EditorPrefs.GetBool("kAutoRefresh", false), "Auto Refresh"));

        // Call Code //
        dynamic script = new Obj(DllFullPath(), "GUI");
        script.OnGUI();
    }

    //======================================================================================================================================================//
    public static Microsoft.CodeAnalysis.SyntaxTree Parse(string text, string filename = "", CSharpParseOptions options = null) {
        return SyntaxFactory.ParseSyntaxTree(text, options, filename);
    }

    //======================================================================================================================================================//
    public static void Run(string path, string type, string method) {
        try {
            var assembly = Assembly.LoadFrom(path);
            var t = assembly.GetType(type);
            var m = t.GetMethod(method);
            m.Invoke(null, null);
        }
        catch (Exception ex) {

        }
    }

    //======================================================================================================================================================//
    void Compile() {
        string script;
        try {
            script = File.ReadAllText(ScriptPath);
        }
        catch (Exception) {
            return;
        }

        if (script == LastScript)
            return;

        UpdateDll();
        LastScript = script;

        var tree = Parse(script, "", CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp6));
        var compilation = CSharpCompilation.Create(LatestDllName, new Microsoft.CodeAnalysis.SyntaxTree[] { tree }, References, Options);

        try {
            /*var stream = new MemoryStream();
            var emitResult = compilation.Emit(stream);

            if (emitResult.Success) {
                stream.Seek(0, SeekOrigin.Begin);
                AssemblyDefinition assdef = new AssemblyDefinition();
                assdef.
            }*/


            var result = compilation.Emit(DllFullPath());
            Result = result.Success ? "Sucess!!" : "Failed";

            if (!result.Success) {
                foreach (var d in result.Diagnostics) {
                    if(d.Severity == DiagnosticSeverity.Error)
                        Result += "\n" + d.Location.GetLineSpan().StartLinePosition + ": " + d.GetMessage();
                }

                DeleteDll();
            }
        }
        catch (Exception ex) {
            //Result = "Failed\n\n" + ex;
            Debug.LogError(ex);
        }
    }

    //======================================================================================================================================================//
    static public string DllFullPath() {
        return DllPath + LatestDllName;
    }

    //======================================================================================================================================================//
    void UpdateDll() {
        DeleteDll();
        LatestDllName = "Script_" + GUID.Generate() + ".dll";
    }

    //======================================================================================================================================================//
    void DeleteDll() {
        try {
            if (File.Exists(DllFullPath()))
                File.Delete(DllFullPath());
        }
        catch (Exception) {          
        }       
    }

    //======================================================================================================================================================//
    void Run() {
        //ProxyDomain pd = new ProxyDomain();

        //AppDomainSetup info = new AppDomainSetup();
        //info.ApplicationBase = Environment.CurrentDirectory;
        //Evidence evidence = AppDomain.CurrentDomain.Evidence;
        //AppDomain domain = AppDomain.CreateDomain("ErikDomain", evidence, info);


        /*Type type = typeof(Proxy);
        var value = (Proxy)domain.CreateInstanceAndUnwrap(
            type.Assembly.FullName,
            type.FullName);*/

        //var assembly = value.GetAssembly(args[0]);


        //AppDomain domain = AppDomain.CreateDomain("ErikDomain");
        //Assembly assembly = domain.Load(DllPath);
        //var an = AssemblyName.GetAssemblyName(DllPath);
        //var assembly = domain.Load(pd.GetAssembly(DllPath).GetName());

        //Assembly assembly = pd.GetAssembly(DllPath);

        if (!File.Exists(LatestDllName))
            return;

        try {
            var assembly = Assembly.LoadFrom(LatestDllName);
            var types = assembly.GetTypes();

            object a = assembly.CreateInstance("Test");
            a.GetType().GetMethod("Wtf").Invoke(a, null);

            //AppDomain.Unload(domain);
            return;




            foreach (Type type in types) {
                Debug.Log(type.Name);
            }

            Type test = assembly.GetType("StaticTest");
            foreach (var method in test.GetMethods()) {
                Debug.Log(method.Name);
            }

            test.GetMethod("Hello").Invoke(null, null);
            //assembly.CreateInstance()
        }
        catch (Exception ex) {
            Debug.Log(ex);
        }
        

    }
}


class ProxyDomain : MarshalByRefObject {
    public Assembly GetAssembly(string path) {
        try {
            return Assembly.LoadFrom(path);
        }
        catch (Exception ex) {
            throw ex;
        }
    }
}


public class Obj : DynamicObject {
    public Dictionary<string, object> Data = new Dictionary<string, object>();
    public Assembly Assembly;
    public Type Type;

    //======================================================================================================================================================//
    public Obj(string path, string typeName = null) {
        try {
            //Debug.Log("Obj: " + path);
            Assembly = Assembly.LoadFrom(path);
            Type = typeName != null ? Assembly.GetType(typeName) : Assembly.GetTypes()[0]; ;
        }
        catch (Exception ex) {
            //Debug.LogError(ex); 
        } 
    }

    //======================================================================================================================================================//
    public override bool TrySetMember(SetMemberBinder binder, object value) {
        Data[binder.Name] = value;
        return true;
    }

    //======================================================================================================================================================//
    public override bool TryGetMember(GetMemberBinder binder, out object result) {
        //Data.TryGetValue(binder.Name, out result);

        result = "----";
        var members = Type.GetMember(binder.Name);

        if (members.Length > 0) {
            if (members[0].MemberType == MemberTypes.Property)
                result = (members[0] as PropertyInfo).GetValue(Type);
            else if (members[0].MemberType == MemberTypes.Field)
                result = (members[0] as FieldInfo).GetValue(Type);
        }


        return true;
    }

    //======================================================================================================================================================//
    public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result) {
        try {
            //GUILayout.Label(binder.Name);


            foreach (var m in Type.GetMembers()) {
                //GUILayout.Label(m.Name);
            }


            var method = Type.GetMethod(binder.Name);
            method.Invoke(null, null);
        }
        catch (Exception ex) {

        }

        result = null;
        return true;
        //return base.TryInvokeMember(binder, args, out result);
    }
}