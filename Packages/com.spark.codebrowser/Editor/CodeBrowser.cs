using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.IO;
using UnityEditor.PackageManager.Requests;
using UnityEditor.PackageManager;
using UnityEditor.Compilation;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using System.Text;
using System.Threading;

public class CodeBrowser : EditorWindow {
    static public GUISkin Skin;
    static public GUISkin SceneSkin;

    const string PACKAGE_PATH = "Packages/com.spark.codebrowser/";
    const string STYLES_PATH = PACKAGE_PATH + "Styles/";
    string LastPropName;
    Vector2 ScrollPos;
    string Filter = "";
    Dictionary<int, bool> HeaderCollapse = new Dictionary<int, bool>();
    static public string CodeSnippet = "";
    static public string CodeFull = "";
    Node LastOpenedNode;

    class PrefsType { public bool Methods, Properties, Fields, Events, Types, Public, Private, Instance, Static, Declared, Mono; }
    class IconsType { public Texture CodeBrowser, Method, Field, Property, Event, Type, Constructor; }
    class StylesType { public GUIStyle Panel, PanelLabel, PropertyHorizontal, PropertyLabel, Arrow, Icon; }
    PrefsType Prefs = new PrefsType();
    IconsType Icons = new IconsType();
    StylesType Styles = new StylesType();

    Dictionary<string, string> TypeAliases = new Dictionary<string, string> {
        { "Boolean", "bool" },
        { "Byte", "byte" },
        { "Char", "char" },
        { "Double", "double" },
        { "Single", "float" },
        { "Int32", "int" },
        { "UInt32", "uint" },
        { "Int64", "long" },
        { "UInt64", "ulong" },
        { "Int16", "short" },
        { "UInt16", "ushort" },
        { "Object", "object" },
        { "String", "string" },
    };

    Dictionary<MemberTypes, Texture> TypeIcon;
    Dictionary<MemberTypes, Texture> MemberIcon;

    PopupExample Popup = new PopupExample();
    Rect PopupRect;


    [Flags]
    enum OptionEnum { Public = 1, Private = 2, Instance = 4, Static = 8, Builtin = 16 };
    Enum opt = OptionEnum.Public | OptionEnum.Private | OptionEnum.Instance;
    enum SortEnum { None, Type, Abc };
    SortEnum Sorting = SortEnum.Type;
    bool LeftDown;

    Vector2 scroll, CodeScroll;
    GUIStyle FoldoutStyle;
    List<bool> Folds = new List<bool>();
    int c;
    Dictionary<int, List<Node>> ScriptClasses = new Dictionary<int, List<Node>>();
    static public Node HoverNode;
    MemberTypes OverType;

    //====================================================================================================//
    [MenuItem("Window/Code Browser")]
    public static void ShowCodeBrowser() {
        EditorWindow window = GetWindow(typeof(CodeBrowser));
        window.Show();
    }

    //====================================================================================================//
    void OnEnable() {
        Skin = AssetDatabase.LoadAssetAtPath<GUISkin>(STYLES_PATH + "CodeBrowser.guiskin");
        SceneSkin = EditorGUIUtility.GetBuiltinSkin(EditorSkin.Scene);

        // Load Styles //
        foreach (FieldInfo field in Styles.GetType().GetFields()) {
            GUIStyle style = Skin.GetStyle(field.Name);
            field.SetValue(Styles, style);
        }

        // Load Icons //
        foreach (FieldInfo field in Icons.GetType().GetFields()) {
            Texture icon = AssetDatabase.LoadAssetAtPath<Texture>(STYLES_PATH + field.Name + ".png");
            field.SetValue(Icons, icon);
        }

        // Load Preferences //
        foreach (FieldInfo field in Prefs.GetType().GetFields()) {
            bool v = EditorPrefs.GetBool("CodeBrowser_" + field.Name, true);
            field.SetValue(Prefs, v);
        }

        titleContent = new GUIContent("Code Browser", Icons.CodeBrowser);
        LoadScriptNodes();
        wantsMouseMove = true;

        TypeIcon = new Dictionary<MemberTypes, Texture> {
            { MemberTypes.Custom, Icons.CodeBrowser },
            { MemberTypes.Method, Icons.Method },
            { MemberTypes.Property, Icons.Property },
            { MemberTypes.Field, Icons.Field },
            { MemberTypes.Event, Icons.Event },
            { MemberTypes.NestedType, Icons.Type }
        };

        MemberIcon = new Dictionary<MemberTypes, Texture> {
            { MemberTypes.Method, Icons.Method },
            { MemberTypes.Property, Icons.Property },
            { MemberTypes.Field, Icons.Field },
            { MemberTypes.Event, Icons.Event },
            { MemberTypes.Constructor, Icons.Constructor },
            { MemberTypes.NestedType, Icons.Type }
        };
    }

    //====================================================================================================//
    void OnDisable() {
        // Save Settings //
        foreach (FieldInfo pref in Prefs.GetType().GetFields()) {
            bool v = (bool)pref.GetValue(Prefs);
            EditorPrefs.SetBool("CodeBrowser_" + pref.Name, v);
        }

        Destroy(this);
    }

    //====================================================================================================//
    void OnSelectionChange() {
        LoadScriptNodes();
        Repaint();
    }

    static ListRequest Request;
    static void Progress() {
        if (Request.IsCompleted) {
            if (Request.Status == StatusCode.Success)
                foreach (var package in Request.Result)
                    Debug.Log("Package name: " + package.name);
            else if (Request.Status >= StatusCode.Failure)
                Debug.Log(Request.Error.message);

            EditorApplication.update -= Progress;
        }
    }

    (bool Public, bool Static) GetModifiers(SyntaxTokenList modifiers) {
        List<string> m = new List<string>();
        m = modifiers.Select(m => m.ToString()).ToList();

        return (m.Contains("public"), m.Contains("static"));
    }

    //====================================================================================================//
    void ScriptNode(SyntaxNode node, int level, bool visible) {
        //if (node.Kind() == SyntaxKind.ClassDeclaration || node.Kind() == SyntaxKind.MethodDeclaration) {
        //string indent = new string('-', level * 4);
        //EditorGUILayout.TextField(node.Kind().ToString(), indent + node.ToString());
        //}

        if (c >= Folds.Count)
            Folds.Add(true);

        var location = node.GetLocation();
        int line = location.GetLineSpan().StartLinePosition.Line + 1;

        if (node.Kind() == SyntaxKind.ClassDeclaration) {
            ClassDeclarationSyntax cla = (ClassDeclarationSyntax)node;
            EditorGUILayout.LabelField("Class: " + cla.Identifier + " - " + line);
        }
        else if (node.Kind() == SyntaxKind.MethodDeclaration) {
            MethodDeclarationSyntax method = (MethodDeclarationSyntax)node;
            EditorGUILayout.LabelField("Method: " + method.Identifier + " - " + line);
        }
        else if (node.Kind() == SyntaxKind.FieldDeclaration) {
            FieldDeclarationSyntax field = (FieldDeclarationSyntax)node;
            var mods = GetModifiers(field.Modifiers);

            foreach (var variable in field.Declaration.Variables) {
                EditorGUILayout.LabelField("Field: " + variable.Identifier + " - " + line + " - " + mods.Public);
            }
        }
        else if (node.Kind() == SyntaxKind.PropertyDeclaration) {
            PropertyDeclarationSyntax prop = (PropertyDeclarationSyntax)node;
            var mods = GetModifiers(prop.Modifiers);

            EditorGUILayout.LabelField("Property: " + prop.Identifier + " - " + line + " - " + mods.Public);
        }

        if (node.ChildNodes().Count() == 0) {
            string text = node.ToString();
            if (node.Kind() == SyntaxKind.MethodDeclaration) {


                text = "<color=cyan>" + text + "</color>";

            }
            CodeSnippet += text + "\n";
        }


        //if (visible)
        //Folds[c] = EditorGUILayout.Foldout(Folds[c], "<b><color=cyan>" + node.Kind().ToString() + "</color></b> : " + node.ToString().Replace("\n", ""), FoldoutStyle);



        bool v = visible && Folds[c];
        c++;
        //EditorGUI.indentLevel++;   

        foreach (var child in node.ChildNodes()) {
            ScriptNode(child, level + 1, v);
        }

        //EditorGUI.indentLevel--;
    }

    //====================================================================================================//
    void OnGUI() {
        // Call Code //
        dynamic script = new Obj(CompilerTool.DllFullPath(), "GUI");
        script.CodeBrowser();


        FoldoutStyle = new GUIStyle(SceneSkin.GetStyle("Foldout"));
        FoldoutStyle.clipping = TextClipping.Clip;
        FoldoutStyle.wordWrap = false;
        FoldoutStyle.richText = true;
        FoldoutStyle.fixedWidth = 800;
        FoldoutStyle.stretchWidth = true;
        //FoldoutStyle.fontSize = 10;

        //if (GUILayout.Button("List Packages")) {
        //Request = Client.List();
        //Client.
        //EditorApplication.update += Progress;
        //}

        if (GUILayout.Button("Application"))
            ReflectionObject = typeof(Application);
        if (GUILayout.Button("Editor"))
            ReflectionObject = typeof(EditorApplication);
        if (GUILayout.Button("GUI"))
            ReflectionObject = typeof(GUI);
        if (GUILayout.Button("Selection"))
            ReflectionObject = Selection.activeObject;

        GUILayout.TextField(ReflectionObject.ToString());

        if (Selection.activeObject != null) {
            UnityEngine.Object obj = Selection.activeObject;
            Type type = obj.GetType();
            //EditorGUILayout.TextField("Type", type.Name);

            if (type == typeof(MonoScript)) {
                //var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Player);
                //var sourcefiles = assemblies
                //    .SelectMany(assembly => assembly.sourceFiles)
                //    .Where(file => !string.IsNullOrEmpty(file) && file.StartsWith("Assets"));

                //foreach (var f in assemblies) {
                //    EditorGUILayout.LabelField("Assembly: " + f.name);
                //}

                //AssetDatabase.LoadMainAssetAtPath();
                //LoadScriptNodes(obj);
            }
        }

        GUI.contentColor = Color.white;
        ToolbarGUI();
        CodeSnippet = "";

        ScrollPos = GUILayout.BeginScrollView(ScrollPos);
        {
            ReflectionGUI(ReflectionObject);



            foreach (var obj in Selection.objects) {
                //ReflectionGUI(obj);
                continue;

                Type type = obj.GetType();
                if (type == typeof(GameObject))
                    ObjectGUI((GameObject)obj);
                else if (type == typeof(MonoScript))
                    ScriptGUI((MonoScript)obj);
                else
                    EditorGUILayout.LabelField(obj.name + " : " + obj.GetType().Name);
            }

            GUILayout.FlexibleSpace();
        }
        GUILayout.EndScrollView();

        //GUI.backgroundColor = new Color(0, 0, 0, 0.5f);
        //CodeScroll = GUILayout.BeginScrollView(CodeScroll);
        //EditorGUILayout.TextArea(CodeFull, Skin.textArea, GUILayout.MinHeight(20));
        //GUILayout.EndScrollView();

        // Floating //
        var ev = Event.current;
        Rect panel = new Rect(Vector2.zero, position.size);
        bool over = panel.Contains(ev.mousePosition);

        if (ev.type == EventType.MouseDown && ev.button == 0)
            LeftDown = true;
        else if (ev.type == EventType.MouseUp && ev.button == 0)
            LeftDown = false;

        if (over) {
            if (CodeSnippet != "" && !LeftDown) {
                GUI.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.9f);
                Vector2 size = Skin.textArea.CalcSize(new GUIContent(CodeSnippet));
                Rect popupRect = new Rect(Event.current.mousePosition - new Vector2(0, 0), size);
                GUI.TextArea(popupRect, CodeSnippet, Skin.textArea);
                GUI.backgroundColor = Color.white;
            }
        }
        else if (ev.type == EventType.Repaint)
            LeftDown = false;
    }

    //====================================================================================================//
    void LoadScriptNodes() {
        double start = EditorApplication.timeSinceStartup;

        foreach (GameObject obj in Selection.gameObjects) {
            Component[] components = obj.GetComponents<Component>();
            foreach (Component component in components) {
                if (!(component is MonoBehaviour))
                    continue;

                MonoBehaviour mono = (MonoBehaviour)component;
                MonoScript script = MonoScript.FromMonoBehaviour(mono);

                // Roslyn // 
                Microsoft.CodeAnalysis.SyntaxTree tree = CSharpSyntaxTree.ParseText(script.text);
                CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
                MetadataReference mscorlib = MetadataReference.CreateFromFile(typeof(object).GetTypeInfo().Assembly.Location);
                var compilation = CSharpCompilation.Create("Loader", syntaxTrees: new[] { tree }, references: new[] { mscorlib });
                var model = compilation.GetSemanticModel(tree);

                // Walk Nodes //
                var data = new ScriptWalker(component, model);
                data.Visit(root);
                ScriptClasses[script.GetInstanceID()] = data.Nodes;
                CodeFull = data.Text.ToString();
                //File.WriteAllText("c:/Spark/Types.txt", data.Text.ToString());
            }
        }
        //Debug.Log("Time: " + (EditorApplication.timeSinceStartup - start).ToString("N2") + "s");
    }

    //====================================================================================================//
    void ToolbarGUI() {
        EditorGUILayout.BeginVertical();
        {
            {
                //if (GUILayout.Button("Stack Test")) {

                // Find DLL for method //
                // Load DLL //
                // Get Lines //

                //Mono.Cecil.Cil.

                //string filename = @"C:\open\fbsource\arvr\projects\pioneer\AugmentedCalling\AugmentedCalling\Library\ScriptAssemblies\com.orion.aw.pong.";
                //string pdb = filename + "pdb";
                //string dll = filename + "dll";
                //var r = new ReaderParameters { ReadSymbols = true };
                //var ass = AssemblyDefinition.ReadAssembly(dll, r);

                //foreach (var module in ass.Modules) {
                //    Debug.Log(module.Name);



                //    foreach (TypeDefinition t in module.Types) {
                //        Debug.Log("-------------------------" + t.Name + "-------------------------");

                //        if (t.Name != "PongPaddleController")
                //            continue;


                //        foreach (var f in t.Fields) {
                //            //Debug.Log("   - " + f.);
                //            //var token = f.MetadataToken;
                //            //module.CustomDebugInformations
                //        }


                //        foreach (MethodDefinition method in t.Methods) {



                //            var mapping = method.DebugInformation.GetSequencePointMapping();
                //            foreach (var m in mapping) {
                //                //Debug.Log("   - " + m.Key + " - " + m.Value.StartLine);
                //            }

                //            int line = 0;
                //            string path = "";
                //            if (method.DebugInformation.SequencePoints.Count > 0) {
                //                var point = method.DebugInformation.SequencePoints[0];
                //                line = point.StartLine;
                //                path = point.Document.Url;
                //                //Debug.Log("    - " + point.Document.Url + " : " + point.StartLine + " : " + point.EndLine + " : " + point.Offset);
                //            }

                //            Debug.Log("" + method.Name + " - " + path + " : " + line);

                //            if (GUILayout.Button(t.Name + "." + method.Name)) {
                //                int start = path.IndexOf("Packages");

                //                string relativePath = path.Substring(start).Replace("\\", "/");
                //                UnityEngine.Object obj = AssetDatabase.LoadMainAssetAtPath(relativePath);
                //                Debug.Log("Object: " + obj.name);

                //                Debug.Log(path);
                //                Debug.Log(relativePath);

                //                AssetDatabase.OpenAsset(obj, line);
                //                //MonoBehaviour mono = mono
                //                //MonoScript script = MonoScript.FromMonoBehaviour(mono);
                //                /*string path = AssetDatabase.GetAssetPath(script);
                //                GUI.color = File.Exists(Path.GetFullPath(path)) ? Color.white : Color.gray;
                //                if (GUILayout.Button(EditorGUIUtility.TrIconContent("cs Script Icon"), Styles.Icon)) {
                //                    Application.OpenURL(Path.GetFullPath(path));
                //                }*/
                //                //EditorGUILayout.ObjectField(script, script.GetType(), true, GUILayout.MinWidth(100), GUILayout.MaxWidth(200));
                //            }

                //            /*foreach (Mono.Cecil.Cil.SequencePoint point in method.DebugInformation.SequencePoints) {
                //                Debug.Log("    - " + point.Document.Url + " : " + point.StartLine + " : " + point.EndLine + " : " + point.Offset);
                //            }*/
                //        }
                //    }
                //}
                //  }
            }

            GUI.backgroundColor = new Color(0.8f, 0.8f, 0.8f);
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                GUI.backgroundColor = new Color(0.75f, 0.75f, 0.75f);

                Prefs.Methods = GUILayout.Toggle(Prefs.Methods, Icons.Method, EditorStyles.toolbarButton, GUILayout.Width(28));
                Prefs.Properties = GUILayout.Toggle(Prefs.Properties, Icons.Property, EditorStyles.toolbarButton, GUILayout.Width(26));
                Prefs.Fields = GUILayout.Toggle(Prefs.Fields, Icons.Field, EditorStyles.toolbarButton, GUILayout.Width(26));
                Prefs.Events = GUILayout.Toggle(Prefs.Events, Icons.Event, EditorStyles.toolbarButton, GUILayout.Width(26));
                Prefs.Types = GUILayout.Toggle(Prefs.Types, Icons.Type, EditorStyles.toolbarButton, GUILayout.Width(26));
                //Prefs.Constructor = GUILayout.Toggle(Prefs.Constructor, ConstructorIcon, EditorStyles.toolbarButton, GUILayout.Width(26));

                //Prefs.Public = GUILayout.Toggle(Prefs.Public, "Public", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false));
                //Prefs.Instance = GUILayout.Toggle(Prefs.Instance, "Instance", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false));
                //Prefs.Declared = GUILayout.Toggle(Prefs.Declared, "Declared", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false));
                //Prefs.Static = GUILayout.Toggle(Prefs.Static, "Static", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false));
                //Prefs.Private = GUILayout.Toggle(Prefs.Private, "Private", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false));
                //Prefs.Mono = GUILayout.Toggle(Prefs.Mono, "Unity", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false));

                GUI.backgroundColor = Color.white;

                opt = EditorGUILayout.EnumFlagsField(opt, SceneSkin.GetStyle("ToolbarPopup"), GUILayout.MaxWidth(80));
                Prefs.Public = opt.HasFlag(OptionEnum.Public);
                Prefs.Private = opt.HasFlag(OptionEnum.Private);
                Prefs.Instance = opt.HasFlag(OptionEnum.Instance);
                Prefs.Static = opt.HasFlag(OptionEnum.Static);
                Prefs.Mono = opt.HasFlag(OptionEnum.Builtin);

                Sorting = (SortEnum)EditorGUILayout.EnumPopup(Sorting, SceneSkin.GetStyle("ToolbarPopup"), GUILayout.MaxWidth(80));


                //GUI.backgroundColor = new Color(0.75f, 0.75f, 0.75f);

                /*if (GUILayout.Button("Popup Options", SceneSkin.GetStyle("ToolbarPopup"))) {
                    //PopupWindow.Show(buttonRect, Popup);
                    
                }*/
                if (Event.current.type == EventType.Repaint) PopupRect = GUILayoutUtility.GetLastRect();

                GUILayout.FlexibleSpace();
                Filter = EditorGUILayout.TextArea(Filter, SceneSkin.GetStyle("ToolbarSeachTextField"), GUILayout.MaxWidth(200));


                GUI.backgroundColor = Color.white;
            }
            EditorGUILayout.EndHorizontal();

            Rect r = GUILayoutUtility.GetLastRect();
            r.height = 1;

            EditorGUI.DrawRect(r, new Color(0.15f, 0.15f, 0.15f));

        }
        EditorGUILayout.EndVertical();
    }

    //====================================================================================================//
    void ObjectGUI(GameObject obj) {
        int ID = obj.GetInstanceID();
        if (!HeaderCollapse.ContainsKey(ID))
            HeaderCollapse[ID] = true;

        GUI.backgroundColor = new Color(0, 0, 0, 0.2f);
        EditorGUILayout.BeginVertical(Styles.Panel);
        {
            GUI.backgroundColor = new Color(1, 1, 1, 1f);
            EditorGUILayout.BeginHorizontal();
            {
                HeaderCollapse[ID] = GUILayout.Toggle(HeaderCollapse[ID], "", Styles.Arrow);
                GUILayout.Button(EditorGUIUtility.TrIconContent("GameObject Icon"), Styles.Icon);
                GUILayout.Label(obj.name, Styles.PanelLabel);
                //EditorGUILayout.ObjectField(obj, obj.GetType(), true, GUILayout.MinWidth(50), GUILayout.MaxWidth(200));
            }
            EditorGUILayout.EndHorizontal();

            if (HeaderCollapse[ID]) {
                EditorGUI.indentLevel++;
                Component[] components = obj.GetComponents<Component>();

                foreach (Component c in components) {
                    if (c is MonoBehaviour && Prefs.Mono) {
                        MonoScript script = MonoScript.FromMonoBehaviour((MonoBehaviour)c);
                        ScriptGUI(script);
                    }
                }

                EditorGUI.indentLevel--;
            }
        }
        EditorGUILayout.EndVertical();
    }

    //====================================================================================================//
    public object ReflectionObject = typeof(Application);

    void ReflectionGUI(object obj) {
        EditorGUI.indentLevel++;
        EditorGUIUtility.wideMode = true;
        EditorGUIUtility.labelWidth = 130;

        int ID = obj.GetHashCode();
        if (!HeaderCollapse.ContainsKey(ID))
            HeaderCollapse[ID] = true;

        GUI.backgroundColor = new Color(0, 0, 0, 0.2f);
        EditorGUILayout.BeginVertical(Skin.box);
        {
            GUI.backgroundColor = new Color(1, 1, 1);
            EditorGUILayout.BeginHorizontal();
            {
                HeaderCollapse[ID] = GUILayout.Toggle(HeaderCollapse[ID], "", Styles.Arrow);

                if(obj is UnityEngine.Object)
                    GUILayout.Button(EditorGUIUtility.ObjectContent((UnityEngine.Object)obj, null), Styles.Icon);
                else
                    GUILayout.Button(Icons.CodeBrowser, Styles.Icon);

                GUILayout.Label(obj.ToString(), Styles.PanelLabel);
                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.EndHorizontal();

            var type = obj is Type ? (Type)obj : obj.GetType();
            var members = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);

            // Assembly -> Modules
            // Module -> Types
            // Type -> Members

            if (HeaderCollapse[ID]) {

                if (GUI.Button(GetIndentedControlRect(), "Assembly: " + type.Assembly.FullName))
                    ReflectionObject = type.Assembly;
                if (GUI.Button(GetIndentedControlRect(), "Module: " + type.Module.Name))
                    ReflectionObject = type.Module;
                if (GUI.Button(GetIndentedControlRect(), "Base: " + type.BaseType?.Name))
                    ReflectionObject = type.BaseType;


                if (obj is System.Reflection.Assembly) {
                    var assembly = (System.Reflection.Assembly)obj;
                    var modules = assembly.GetModules();
                    foreach (var m in modules) {
                        var name = m.Name;
                        var typeName = m.GetType().Name;
                        //name += " : <color=#FF9>" + typeName + "</color>";

                        EditorGUILayout.BeginHorizontal(Styles.PropertyHorizontal);
                        {
                            //GUI.color = isPublic ? Color.white : new Color(1, 1, 1, 0.4f);
                            EditorGUILayout.LabelField(new GUIContent(Icons.CodeBrowser), Styles.Icon, GUILayout.Width(16));
                            //EditorGUILayout.LabelField(name, Styles.PropertyLabel);
                            if (GUI.Button(GetIndentedControlRect(), name, Styles.PropertyLabel))
                                ReflectionObject = m;

                            //GUI.color = Color.white;
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }

                if (obj is Module) {
                    var module = (Module)obj;
                    var types = module.GetTypes();
                    foreach (var t in types) {
                        if (!t.IsPublic)
                            continue;

                        var name = t.Name;
                        var typeName = t.GetType().Name;
                        //name += " : <color=#FF9>" + typeName + "</color>";

                        EditorGUILayout.BeginHorizontal(Styles.PropertyHorizontal);
                        {
                            //GUI.color = isPublic ? Color.white : new Color(1, 1, 1, 0.4f);
                            EditorGUILayout.LabelField(new GUIContent(Icons.CodeBrowser), Styles.Icon, GUILayout.Width(16));
                            //EditorGUILayout.LabelField(name, Styles.PropertyLabel);
                            if (GUI.Button(GetIndentedControlRect(), name, Styles.PropertyLabel))
                                ReflectionObject = t;

                            //GUI.color = Color.white;
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }


                foreach (var m in members) {
                    if (m.Name == LastPropName)
                        continue;

                    LastPropName = m.Name;
                    var name = m.Name;
                    var typeName = m.GetType().Name;
                    bool isPublic = true;
                    bool isStatic = false;
                    Type memberType = null;
                    object value = null;



                    if (m.MemberType == MemberTypes.Method) {
                        if (!Prefs.Methods) continue;

                        var method = (MethodInfo)m;
                        isPublic = method.IsPublic;
                        isStatic = method.IsStatic;
                        typeName = method.ReturnType.Name;
                        memberType = method.ReturnType; 
                    }
                    else if (m.MemberType == MemberTypes.Property) {
                        if (!Prefs.Properties) continue;

                        var prop = (PropertyInfo)m;
                        var get = prop.GetGetMethod();
                        isPublic = get != null ? get.IsPublic : false;
                        isStatic = get != null ? get.IsStatic : false;
                        typeName = prop.PropertyType.Name;
                        memberType = prop.PropertyType;
                        //value = get != null ? get.Invoke(obj, null) : null;
                        try {
                            value = prop.GetValue(obj);
                        }
                        catch (Exception e) {
                            value = e.Message;
                        }
                    }
                    else if (m.MemberType == MemberTypes.Field) {
                        if (!Prefs.Fields) continue;

                        var field = (FieldInfo)m;
                        isPublic = field.IsPublic;
                        typeName = field.FieldType.Name;
                        memberType = field.FieldType;

                        try {
                            value = field.GetValue(obj);
                        }
                        catch (Exception e) {
                            value = e.Message;
                        }
                    }
                    else if (m.MemberType == MemberTypes.NestedType) {
                        if (!Prefs.Types) continue;

                        typeName = m.ToString();
                        memberType = (Type)m;
                    }


                    name += " : <color=#FF9>" + typeName + "</color>";
                    name += value != null ? " = <color=#9F9>" + value?.ToString() + "</color>" : "";

                    // Node n = new Node(name, m.MemberType, null, "", isPublic, isStatic);
                    //PropertyGUI(n, null);

                    bool showPublic = isPublic && Prefs.Public;
                    bool showPrivate = !isPublic && Prefs.Private;
                    bool showStatic = isStatic && Prefs.Static;
                    bool showInstance = !isStatic && Prefs.Instance;
                    if (!((showPublic || showPrivate) && (showInstance || showStatic)))
                        continue;

                    EditorGUILayout.BeginHorizontal(Styles.PropertyHorizontal);
                    {
                        GUI.color = isPublic ? Color.white : new Color(1, 1, 1, 0.4f);
                        EditorGUILayout.LabelField(new GUIContent(MemberIcon[m.MemberType]), Styles.Icon, GUILayout.Width(16));
                        //EditorGUILayout.LabelField(name, Styles.PropertyLabel);
                        if (GUI.Button(GetIndentedControlRect(), name, Styles.PropertyLabel))
                            ReflectionObject = value != null ? value : memberType;

                        GUI.color = Color.white;
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
        }

        EditorGUILayout.EndVertical();
        EditorGUI.indentLevel--;
    }

    //====================================================================================================//
    void ScriptGUI(MonoScript script) {
        EditorGUI.indentLevel++;
        EditorGUIUtility.wideMode = true;
        EditorGUIUtility.labelWidth = 130;

        int ID = script.GetInstanceID();
        if (!HeaderCollapse.ContainsKey(ID))
            HeaderCollapse[ID] = true;

        GUI.backgroundColor = new Color(0, 0, 0, 0.2f);
        EditorGUILayout.BeginVertical(Skin.box);
        {
            GUI.backgroundColor = new Color(1, 1, 1);
            EditorGUILayout.BeginHorizontal();
            {             
                HeaderCollapse[ID] = GUILayout.Toggle(HeaderCollapse[ID], "", Styles.Arrow);
                GUILayout.Button(EditorGUIUtility.ObjectContent(script, null), Styles.Icon);
                GUILayout.Label(script.GetType().Name, Styles.PanelLabel);
                GUILayout.FlexibleSpace();

                if (GUILayout.Button(SceneSkin.GetStyle("IN ObjectField").normal.background, Styles.Icon)) {
                    EditorGUIUtility.PingObject(script);
                    Selection.activeObject = script;
                }
            }
            EditorGUILayout.EndHorizontal();

            if (HeaderCollapse[ID]) {
                if (ScriptClasses.ContainsKey(ID)) {
                    foreach (var c in ScriptClasses[ID]) {
                        EditorGUILayout.BeginHorizontal(Styles.PropertyHorizontal);
                        EditorGUILayout.LabelField(new GUIContent(Icons.CodeBrowser), Styles.Icon, GUILayout.Width(16));
                        EditorGUILayout.LabelField(c.Name, Styles.PropertyLabel);
                        EditorGUILayout.EndHorizontal();

                        // Sorting //
                        List<Node> sorted = new List<Node>(c.Nodes);
                        if (Sorting == SortEnum.Type) 
                            sorted.Sort((a, b) => a.Type.CompareTo(b.Type));             
                        else if (Sorting == SortEnum.Abc) 
                            sorted.Sort((a, b) =>  a.Name.CompareTo(b.Name));                    

                        EditorGUI.indentLevel++;
                        foreach (var member in sorted) {
                            PropertyGUI(member, null);
                            //EditorGUILayout.LabelField("    - " + member.Name + " : " + member.Type + " [" + member.StartLine + "-" + member.EndLine + "]");
                        }
                        EditorGUI.indentLevel--;
                    }
                }
            }
        }

        EditorGUILayout.EndVertical();
        EditorGUI.indentLevel--;
    }

    //====================================================================================================//  
    void PropertyGUI(Node prop, Component component) {
        if (prop.Name.StartsWith("get_") || prop.Name.StartsWith("set_"))
            return;

        if (prop.Name == LastPropName)
            return;

        if (prop.Name.StartsWith("<"))
            return;

        if (!prop.Name.ToLower().Contains(Filter.ToLower()))
            return;

        bool showPublic = prop.isPublic && Prefs.Public;
        bool showPrivate = !prop.isPublic && Prefs.Private;
        bool showStatic = prop.isStatic && Prefs.Static;
        bool showInstance = !prop.isStatic && Prefs.Instance;
        if (!((showPublic || showPrivate) && (showInstance || showStatic)))
            return;

        if (!(prop.Type == MemberTypes.Method && Prefs.Methods || prop.Type == MemberTypes.Property && Prefs.Properties || prop.Type == MemberTypes.Field && Prefs.Fields || prop.Type == MemberTypes.Event && Prefs.Events))
            return;
    
        string text = prop.Name;
        LastPropName = text;

        // Hover //
        Rect rect = GUILayoutUtility.GetLastRect();
        rect = new Rect(rect.x, rect.y + 0 + 14, rect.width, rect.height - 0);
        bool over = false;

        if (rect.Contains(Event.current.mousePosition)) {
            over = true;
            OverType = prop.Type;
            CodeSnippet = prop.Docs == "" ? prop.Code : string.Join("\n\n", prop.Docs, prop.Code);
            HoverNode = prop;

            //if(LeftDown)
            //    OpenScript(prop, component);
        }

        // Draw //
        EditorGUILayout.BeginHorizontal(Styles.PropertyHorizontal);
        {
            if (prop.Type == MemberTypes.Field || prop.Type == MemberTypes.Property)
                text += "  <size=10><color=" + (prop.isPublic ? "#777" : "#555") + ">" + prop.DataType + "</color></size>";

            GUI.color = prop.isPublic ? Color.white : new Color(1, 1, 1, 0.4f);
            GUI.color *= over ? 1.5f : 1;
            EditorGUILayout.LabelField(new GUIContent(TypeIcon[prop.Type]), Styles.Icon, GUILayout.Width(16));

            GUI.Label(GetIndentedControlRect(), text, Styles.PropertyLabel);
            //if (GUI.Button(GetIndentedControlRect(), text, Styles.PropertyLabel))
            //OpenScript(prop, component);

            GUI.color = Color.white;
        }
        EditorGUILayout.EndHorizontal();
    }

    //====================================================================================================//
    void OpenScript(Node prop, Component component) {
        if (prop == LastOpenedNode)
            return;

        if (component is MonoBehaviour) {
            MonoBehaviour mono = (MonoBehaviour)component;
            MonoScript script = MonoScript.FromMonoBehaviour(mono);
            AssetDatabase.OpenAsset(script, prop.StartLine);
            LastOpenedNode = prop;
        }
    }

    //====================================================================================================//
    void OpenScript(MemberInfo prop, Component component, MethodInfo method, bool open) {
        //if (!open && prop.GetHashCode() == LastOpenedNode)
        //    return;

        // Find DLL for method //
        Debug.Log("Load DLL: " + prop.Module.Assembly.Location);
        string filename = prop.Module.Assembly.Location;

        // Load DLL //
        var r = new ReaderParameters { ReadSymbols = true };
        var assembly = AssemblyDefinition.ReadAssembly(filename, r);

        // Get Lines //
        string typeName = component.GetType().FullName;
        TypeDefinition t = assembly.MainModule.GetType(typeName);
        Debug.Log("Type: " + typeName + " : " + t);



        // Find Method //
        foreach (var m in t.Methods) {
            if (m.Name == prop.Name) {
                //Debug.Log("Method: " + m.Name + " : " + t);

                int line = 1;
                int end = 1;
                string path = "";
                if (m.DebugInformation.SequencePoints.Count > 0) {
                    var point = m.DebugInformation.SequencePoints[0];
                    line = point.StartLine;
                    end = m.DebugInformation.SequencePoints[m.DebugInformation.SequencePoints.Count - 1].EndLine;
                    path = point.Document.Url;
                    //Debug.Log("    - " + point.Document.Url + " : " + point.StartLine + " : " + point.EndLine + " : " + point.Offset);
                    //Debug.Log("    - " + point.Document.Url + " : " + start + " : " + end);
                }

                //Debug.Log("" + method.Name + " - " + path + " : " + line);

                // Find Asset //
                if (component is MonoBehaviour) {
                    MonoBehaviour mono = (MonoBehaviour)component;
                    MonoScript script = MonoScript.FromMonoBehaviour(mono);

                    //LastOpenedNode = prop.GetHashCode();
                    //MethodCode = GetMethodCode(script, line, end);

                    if (open)
                        AssetDatabase.OpenAsset(script, line);
                }
            }
        }
    }

    void Update() {
        //if (Event.current.type == EventType.MouseMove)
            Repaint(); 

        //if (Event.current.type == EventType.Repaint)
            //PopupWindow.Show(buttonRect, Popup);
    }

    //====================================================================================================//
    string GetMethodCode(MonoScript script, int start, int end) {
        string path = AssetDatabase.GetAssetPath(script);
        path = Path.GetFullPath(path);
        string code = "";

        if (File.Exists(path)) {
            string[] lines = File.ReadAllLines(path);

            int left = lines[start - 1].Length - lines[start - 1].TrimStart().Length;
            for (int i = start - 1; i < end; i++) {
                string line = lines[i];
                if (line.Length > left)
                    line = line.Substring(left); 

                code += line.Replace("  ", "    ") + (i == end - 1 ? "" : "\n");
            }

            //for (int i = 0; i < lines.Length; i++) {
            //    if (i == start - 1)
            //        code += "<color=white>";

            //    code += lines[i] + "\n";

            //    if (i == end - 1)
            //        code += "</color>";
            //}
        }

        return code;
    }

    //====================================================================================================//
    void SearchScript(MemberInfo prop, Component component, FieldInfo field) {
        if (component is MonoBehaviour) {
            MonoBehaviour mono = (MonoBehaviour)component;
            MonoScript script = MonoScript.FromMonoBehaviour(mono);
            string path = AssetDatabase.GetAssetPath(script);
            path = Path.GetFullPath(path);

            string type = field.FieldType.Name;
            string baseType = type.Replace("[]", "").Replace("`1", "").Replace("`2", "");

            if (File.Exists(path)) {
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++) {
                    if (lines[i].Contains(" " + prop.Name)) {
                        if (lines[i].Contains(" " + baseType) || (TypeAliases.ContainsKey(baseType) && lines[i].Contains(" " + TypeAliases[baseType]))) {
                            AssetDatabase.OpenAsset(script, i + 1);
                            return;
                        }
                    }
                }

                Debug.Log("Not found: " + prop.Name + " - " + type);
            }
        }
    }

    //====================================================================================================//
    Rect GetIndentedControlRect() {
        return EditorGUI.IndentedRect(EditorGUILayout.GetControlRect());
    }
}




public class PopupExample : PopupWindowContent {
    public override Vector2 GetWindowSize() {
        return new Vector2(300, 150);
    }

    public override void OnGUI(Rect rect) {
        GUI.backgroundColor = new Color(0, 0, 0, 0.5f);
        EditorGUI.TextArea(rect, CodeBrowser.CodeSnippet, CodeBrowser.Skin.textArea);
    }

    public override void OnOpen() {
        Debug.Log("Popup opened: " + this);
    }

    public override void OnClose() {
        Debug.Log("Popup closed: " + this);
    }
}
