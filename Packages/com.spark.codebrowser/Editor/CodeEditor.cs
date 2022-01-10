using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
//using Mono.Cecil;
//using Mono.Cecil.Cil;
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

namespace com.meta.codeeditor {
    public class CodeEditor : EditorWindow {
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

        double LastKeyTime;
        NodeTypes OverType;

        //====================================================================================================//
        [MenuItem("Window/Code Editor")]
        public static void ShowCodeEditor() {
            EditorWindow window = GetWindow(typeof(CodeEditor));
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

            titleContent = new GUIContent("Code Editor", Icons.CodeBrowser);
            LoadScriptNodes();
            wantsMouseMove = true;

            EvalText = EditorPrefs.GetString("CodeEditor_EvalText", "Log(123)");
        }

        //====================================================================================================//
        void OnDisable() {
            // Save Settings //
            foreach (FieldInfo pref in Prefs.GetType().GetFields()) {
                bool v = (bool)pref.GetValue(Prefs);
                EditorPrefs.SetBool("CodeBrowser_" + pref.Name, v);
            }

            EditorPrefs.SetString("CodeEditor_EvalText", EvalText);
        }

        //====================================================================================================//
        void OnSelectionChange() {
            LoadScriptNodes();
            RunCode(EvalText);
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
        // var m = GameObject.Find("Canvas").GetType().GetMembers(); foreach(var i in m) Debug.Log("Objects   -   " + i.ToString());
        string EvalText = "";
        string EvalOutput = "";
        Exception EvalException;
        string LastText = "";
        Script<object> EvalScript;
        bool NeedsCompile = false;

        static public string LogBuffer = "";

        static public void Log(object obj) {
            LogBuffer += obj.ToString() + " \n";
        }

        //====================================================================================================//
        async void RunCode(string code, bool recompile = false) {
            try {
                LogBuffer = "";
                if (EvalScript == null || (recompile && EditorApplication.timeSinceStartup - LastKeyTime > 0.2f)) {
                    Debug.Log("Recompile");

                    var options = ScriptOptions.Default
                    .WithImports("System", "System.Reflection", "UnityEngine", "UnityEditor", "com.meta.codeeditor.CodeEditor")
                    .WithReferences(typeof(Transform).Assembly, typeof(Selection).Assembly, typeof(GUILayout).Assembly, typeof(CodeEditor).Assembly, typeof(BindingFlags).Assembly);

                    //var result = await CSharpScript.RunAsync<object>(code, options);
                    //EvalOutput = result.ReturnValue.ToString();
                    //EvalException = null;

                    NeedsCompile = false;
                    EvalScript = CSharpScript.Create(code, options);
                    EvalScript.Compile();
                    await EvalScript.RunAsync();
                }
                else {
                    await EvalScript.RunAsync();
                }
            }
            catch (Exception e) {
                EvalOutput = "";
                EvalException = e;
            }
        }

        //====================================================================================================//
        void OnGUI() {
            var ev = Event.current;
            if (ev.type == EventType.MouseDown && ev.button == 0)
                LeftDown = true;
            else if (ev.type == EventType.MouseUp && ev.button == 0)
                LeftDown = false;
            else if (ev.type == EventType.MouseMove) {
                //LoadScriptNodes();
                //Repaint();
            }

            EvalText = EditorGUILayout.TextArea(EvalText, GUILayout.Height(400));
            GUI.contentColor = EvalException != null ? new Color(1, 0.5f, 0.5f) : Color.white;
            EditorGUILayout.TextArea(EvalException != null ? EvalException.Message : EvalOutput);
            GUI.contentColor = Color.white;

            LogBuffer = EditorGUILayout.TextArea(LogBuffer);
            CodeScroll = GUILayout.BeginScrollView(CodeScroll);
            {

                if (EvalText != LastText) {
                    LastKeyTime = EditorApplication.timeSinceStartup;
                    NeedsCompile = true;
                    LastText = EvalText;
                }
                else
                    RunCode(EvalText, NeedsCompile);
            }
            GUILayout.EndScrollView();

            return;

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
                foreach (GameObject obj in Selection.gameObjects)
                    ObjectGUI(obj);

                GUILayout.FlexibleSpace();
            }
            GUILayout.EndScrollView();

            GUI.backgroundColor = new Color(0, 0, 0, 0.5f);
            //CodeScroll = GUILayout.BeginScrollView(CodeScroll);
            EditorGUILayout.TextArea(CodeFull, Skin.textArea, GUILayout.MinHeight(20));
            //GUILayout.EndScrollView();

            // Floating //
            Rect panel = new Rect(Vector2.zero, position.size);
            bool over = panel.Contains(ev.mousePosition);
            if (ev.type == EventType.Repaint && !over && LeftDown)
                LeftDown = false;

            if (over && CodeSnippet != "" /*&& OverType == NodeTypes.Method*/) {
                GUI.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.9f);

                Vector2 size = Skin.textArea.CalcSize(new GUIContent(CodeSnippet));
                Rect popupRect = new Rect(Event.current.mousePosition - new Vector2(0, 0), size);
                GUI.TextArea(popupRect, CodeSnippet, Skin.textArea);
            }
        }

        //====================================================================================================//
        void LoadScriptNodes() {
            foreach (GameObject obj in Selection.gameObjects) {
                Component[] components = obj.GetComponents<Component>();
                foreach (Component component in components) {
                    if (!(component is MonoBehaviour))
                        continue;

                    MonoBehaviour mono = (MonoBehaviour)component;
                    MonoScript script = MonoScript.FromMonoBehaviour(mono);
                    //string path = AssetDatabase.GetAssetPath(script);
                    //path = Path.GetFullPath(path);

                    // Roslyn // 
                    Microsoft.CodeAnalysis.SyntaxTree tree = CSharpSyntaxTree.ParseText(script.text);
                    CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
                    MetadataReference mscorlib = MetadataReference.CreateFromFile(typeof(object).GetTypeInfo().Assembly.Location);
                    var compilation = CSharpCompilation.Create("MyCompilation", syntaxTrees: new[] { tree }, references: new[] { mscorlib });
                    var model = compilation.GetSemanticModel(tree);

                    // Walk Nodes //
                    var data = new ScriptWalker(component, model);
                    data.Visit(root);
                    foreach (var token in root.DescendantTokens()) {
                        data.VisitToken(token);
                    }

                    ScriptClasses[component.GetInstanceID()] = data.Nodes;
                    CodeFull = data.Text.ToString();
                    //File.WriteAllText("c:/Spark/Types.txt", data.Text.ToString());
                }
            }
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

                    foreach (Component c in components)
                        ComponentGUI(c);

                    EditorGUI.indentLevel--;
                }
            }
            EditorGUILayout.EndVertical();
        }

        //====================================================================================================//
        void ComponentGUI(Component component) {
            // Get States //
            List<MemberInfo> states = new List<MemberInfo>();
            bool isMono = component is MonoBehaviour;

            if (!isMono && !Prefs.Mono)
                return;

            BindingFlags flags = 0;
            if (Prefs.Public) flags |= BindingFlags.Public;
            if (Prefs.Private) flags |= BindingFlags.NonPublic;
            if (Prefs.Instance) flags |= BindingFlags.Instance;
            if (Prefs.Static) flags |= BindingFlags.Static;

            // if (Prefs.Declared) flags |= BindingFlags.DeclaredOnly;
            flags |= BindingFlags.DeclaredOnly;

            MemberInfo[] members = component.GetType().GetMembers(flags);
            foreach (MemberInfo member in members) {
                try {
                    if (!member.IsDefined(typeof(ObsoleteAttribute), true))
                        states.Add(member);
                }
                catch (Exception e) {
                    //Debug.LogError("Component Error: " + e.Message);
                }
            }

            // Draw //
            EditorGUI.indentLevel++;
            EditorGUIUtility.wideMode = true;
            EditorGUIUtility.labelWidth = 130;

            int ID = component.GetInstanceID();
            if (!HeaderCollapse.ContainsKey(ID))
                HeaderCollapse[ID] = isMono;

            GUI.backgroundColor = new Color(0, 0, 0, 0.2f);
            EditorGUILayout.BeginVertical(Skin.box);
            {
                GUI.backgroundColor = new Color(1, 1, 1);
                EditorGUILayout.BeginHorizontal();
                {
                    HeaderCollapse[ID] = GUILayout.Toggle(HeaderCollapse[ID], "", Styles.Arrow);
                    GUILayout.Button(EditorGUIUtility.ObjectContent(component, null), Styles.Icon);
                    GUILayout.Label(component.GetType().Name, Styles.PanelLabel);
                    GUILayout.FlexibleSpace();

                    if (isMono) {
                        MonoBehaviour mono = (MonoBehaviour)component;
                        MonoScript script = MonoScript.FromMonoBehaviour(mono);
                        //EditorGUILayout.ObjectField(script, script.GetType(), true, GUILayout.MinWidth(50), GUILayout.MaxWidth(200));


                        if (GUILayout.Button(SceneSkin.GetStyle("IN ObjectField").normal.background, Styles.Icon)) {
                            EditorGUIUtility.PingObject(script);
                            Selection.activeObject = script;
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();

                if (HeaderCollapse[ID]) {
                    //EditorGUI.indentLevel++;
                    LastPropName = "";

                    foreach (MemberInfo prop in states) {
                        //PropertyGUI(prop, component);
                        /*MemberInfo[] props = member.GetFields();
                        foreach (var prop in props) {
                            PropertyGUI(prop, component);
                        }*/
                    }

                    if (isMono) {
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
                                    sorted.Sort((a, b) => a.Name.CompareTo(b.Name));

                                EditorGUI.indentLevel++;
                                foreach (var member in sorted) {
                                    PropertyGUI(member, component);
                                    //EditorGUILayout.LabelField("    - " + member.Name + " : " + member.Type + " [" + member.StartLine + "-" + member.EndLine + "]");
                                }
                                EditorGUI.indentLevel--;
                            }
                        }

                        //var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Player);
                        //var sourcefiles = assemblies
                        //    .SelectMany(assembly => assembly.sourceFiles)
                        //    .Where(file => !string.IsNullOrEmpty(file) && file.StartsWith("Assets"));

                        //foreach (var f in assemblies) {
                        //    EditorGUILayout.LabelField("Assembly: " + f.name);
                        //}

                        //AssetDatabase.LoadMainAssetAtPath();
                    }

                    //EditorGUI.indentLevel--;
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

            LastPropName = prop.Name;
            string text = prop.Name;// + " <color=#999>" + prop.type + "</color>";

            Rect rect = GUILayoutUtility.GetLastRect();
            rect = new Rect(rect.x, rect.y + 0 + 14, rect.width, rect.height - 0);
            string value = "";
            bool over = false;
            if (rect.Contains(Event.current.mousePosition)) {
                //value = " = <color=#7f7>" + field.GetValue(component)?.ToString() + "</color>";
                over = true;
                OverType = prop.Type;
                CodeSnippet = prop.Code;
                HoverNode = prop;

                if (LeftDown)
                    OpenScript(prop, component);
            }

            //EditorGUILayout.BeginHorizontal(Styles.PropertyHorizontal);
            {
                if (prop.Type == NodeTypes.Method && Prefs.Methods) {
                    EditorGUILayout.BeginHorizontal(Styles.PropertyHorizontal);

                    GUI.color = prop.isPublic ? Color.white : new Color(1, 1, 1, 0.4f);
                    GUI.color *= over ? 1.5f : 1;

                    EditorGUILayout.LabelField(new GUIContent(Icons.Method), Styles.Icon, GUILayout.Width(16));

                    GUIContent content = new GUIContent(text);

                    if (GUI.Button(GetIndentedControlRect(), content, Styles.PropertyLabel))
                        OpenScript(prop, component);

                    Rect last = GUILayoutUtility.GetLastRect();
                    last = new Rect(last.x, last.y + 2, last.width, last.height - 4);
                    /*if (last.Contains(Event.current.mousePosition)) {
                        OpenScript(prop, component, method, false);
                        Repaint();
                    }*/

                    GUI.color = Color.white;
                    EditorGUILayout.EndHorizontal();
                }
                else if (prop.Type == NodeTypes.Property && Prefs.Properties) {
                    EditorGUILayout.BeginHorizontal(Styles.PropertyHorizontal);

                    text = prop.Name + "  <size=10><color=" + (prop.isPublic ? "#777" : "#555") + ">" + prop.Name + "</color></size>";

                    GUI.color = prop.isPublic ? Color.white : new Color(1, 1, 1, 0.4f);
                    GUI.color *= over ? 1.5f : 1;

                    EditorGUILayout.LabelField(new GUIContent(Icons.Property), Styles.Icon, GUILayout.Width(16));
                    EditorGUILayout.LabelField(text, Styles.PropertyLabel);
                    GUI.color = Color.white;
                    EditorGUILayout.EndHorizontal();
                }
                else if (prop.Type == NodeTypes.Field && Prefs.Fields) {
                    EditorGUILayout.BeginHorizontal(Styles.PropertyHorizontal);
                    //text = prop.Name + "  <size=10><color=" + (field.IsPublic ? "#777" : "#555") + ">" + field.FieldType.Name + "</color></size>";
                    text = prop.Name;
                    if (true)
                        text += "  <size=10><color=" + (prop.isPublic ? "#777" : "#555") + ">" + prop.DataType + "</color></size>" + value;

                    GUI.color = prop.isPublic ? Color.white : new Color(1, 1, 1, 0.4f);
                    GUI.color *= over ? 1.5f : 1;

                    EditorGUILayout.LabelField(new GUIContent(Icons.Field), Styles.Icon, GUILayout.Width(16));
                    if (GUI.Button(GetIndentedControlRect(), text, Styles.PropertyLabel))
                        OpenScript(prop, component);

                    GUI.color = Color.white;
                    EditorGUILayout.EndHorizontal();
                }
                else if (prop.Type == NodeTypes.Event && Prefs.Events) {
                    EditorGUILayout.BeginHorizontal(Styles.PropertyHorizontal);
                    //text = prop.Name + "  <size=10><color=" + (field.IsPublic ? "#777" : "#555") + ">" + field.FieldType.Name + "</color></size>";
                    text = prop.Name;
                    if (true)
                        text += "  <size=10><color=" + (prop.isPublic ? "#777" : "#555") + ">" + prop.DataType + "</color></size>" + value;

                    GUI.color = prop.isPublic ? Color.white : new Color(1, 1, 1, 0.4f);
                    GUI.color *= over ? 1.5f : 1;

                    EditorGUILayout.LabelField(new GUIContent(Icons.Event), Styles.Icon, GUILayout.Width(16));
                    if (GUI.Button(GetIndentedControlRect(), text, Styles.PropertyLabel)) {
                        // Fix //
                        //SearchScript(prop, component, field);
                    }

                    GUI.color = Color.white;
                    EditorGUILayout.EndHorizontal();
                }
                else {
                    //EditorGUILayout.LabelField(text, Styles.PropertyLabel);
                }
            }
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
            //Debug.Log("Load DLL: " + prop.Module.Assembly.Location);
            //string filename = prop.Module.Assembly.Location;

            //// Load DLL //
            //var r = new ReaderParameters { ReadSymbols = true };
            //var assembly = AssemblyDefinition.ReadAssembly(filename, r);

            //// Get Lines //
            //string typeName = component.GetType().FullName;
            //TypeDefinition t = assembly.MainModule.GetType(typeName);
            //Debug.Log("Type: " + typeName + " : " + t);



            //// Find Method //
            //foreach (var m in t.Methods) {
            //    if (m.Name == prop.Name) {
            //        //Debug.Log("Method: " + m.Name + " : " + t);

            //        int line = 1;
            //        int end = 1;
            //        string path = "";
            //        if (m.DebugInformation.SequencePoints.Count > 0) {
            //            var point = m.DebugInformation.SequencePoints[0];
            //            line = point.StartLine;
            //            end = m.DebugInformation.SequencePoints[m.DebugInformation.SequencePoints.Count - 1].EndLine;
            //            path = point.Document.Url;
            //            //Debug.Log("    - " + point.Document.Url + " : " + point.StartLine + " : " + point.EndLine + " : " + point.Offset);
            //            //Debug.Log("    - " + point.Document.Url + " : " + start + " : " + end);
            //        }

            //        //Debug.Log("" + method.Name + " - " + path + " : " + line);

            //        // Find Asset //
            //        if (component is MonoBehaviour) {
            //            MonoBehaviour mono = (MonoBehaviour)component;
            //            MonoScript script = MonoScript.FromMonoBehaviour(mono);

            //            //LastOpenedNode = prop.GetHashCode();
            //            //MethodCode = GetMethodCode(script, line, end);

            //            if (open)
            //                AssetDatabase.OpenAsset(script, line);
            //        }
            //    }
            //}
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




    public class ScriptWalker : CSharpSyntaxWalker {
        public List<Node> Nodes = new List<Node>();
        public List<string> Usings = new List<string>();
        UnityEngine.Object Module;
        SemanticModel Model;
        Dictionary<string, Type> Types = new Dictionary<string, Type>();
        public StringBuilder Text;

        public enum TokenKind {
            None,
            Keyword,
            Identifier,
            StringLiteral,
            CharacterLiteral,
            Comment,
            DisabledText,
            Region,
            NamedType,
            Namespace,
            Parameter,
            Local,
            Field,
            Property,
        }

        //====================================================================================================//
        public ScriptWalker(UnityEngine.Object obj, SemanticModel model) {
            Module = obj;
            Model = model;
            Text = new StringBuilder();


            /*foreach (var t in obj.GetType().Module.GetTypes()) {
                Types.Add(t.FullName);
                buffer += t.Name + "\n";
                Debug.Log(t.FullName);
            }*/

            Type type = obj.GetType();

            // Find DLL //
            //string filename = type.Module.Assembly.Location;
            //var r = new ReaderParameters { ReadSymbols = true };
            //var assembly = AssemblyDefinition.ReadAssembly(filename, r);

            /*foreach (var reference in assembly.MainModule.AssemblyReferences) {
                //Types.Add(reference.Name + ".");
                buffer += reference.Name + "\n";
            }*/


            foreach (var item in type.GetMembers()) {
                if (item is FieldInfo) {
                    string n = (item as FieldInfo).FieldType.AssemblyQualifiedName;

                    Type t = Type.GetType(n);
                    if (t != null && !Types.ContainsKey(t.Name)) {
                        Types[t.Name] = t;
                        //Debug.Log(n + " : " + t.Name);
                    }
                }

                if (item is PropertyInfo) {
                    string n = (item as PropertyInfo).PropertyType.AssemblyQualifiedName;

                    Type t = Type.GetType(n);
                    if (t != null && !Types.ContainsKey(t.Name)) {
                        Types[t.Name] = t;
                        //Debug.Log(n + " : " + t.Name);
                    }
                }

                //if (item is MethodInfo)
                //    Debug.Log((item as MethodInfo).qu);

                /*foreach (var a in item.GetType().Module.GetTypes()) {
                    Debug.Log(a.AssemblyQualifiedName);
                }*/
            }

            //File.WriteAllText("c:/Spark/Types.txt", buffer);
            //Debug.Log(buffer);
            //Debug.Log("Type Count: " + Types.Count);
        }

        //====================================================================================================//
        public override void VisitUsingDirective(UsingDirectiveSyntax node) {
            Usings.Add(node.Name.ToString());
            //Debug.Log(node.Name.ToString());

            base.VisitUsingDirective(node);
        }

        //====================================================================================================//
        public override void VisitClassDeclaration(ClassDeclarationSyntax node) {
            Nodes.Add(new Node(node.Identifier.ToString(), NodeTypes.Class, node));
            base.VisitClassDeclaration(node);
        }

        //====================================================================================================//
        public override void VisitMethodDeclaration(MethodDeclarationSyntax node) {
            var mods = GetModifiers(node.Modifiers);
            AddtoClass(new Node(node.Identifier.ToString(), NodeTypes.Method, node, mods.Public, mods.Static));
            base.VisitMethodDeclaration(node);
        }

        //====================================================================================================//
        public override void VisitFieldDeclaration(FieldDeclarationSyntax node) {
            foreach (var v in node.Declaration.Variables) {
                var mods = GetModifiers(node.Modifiers);
                string typeName = node.Declaration.Type.ToString();

                // Check if Event //
                bool isEvent = false;

                if (Types.ContainsKey(typeName)) {
                    //Debug.Log("Found: " + typeName + " : " + Types[typeName].AssemblyQualifiedName);
                    if (Types[typeName].IsSubclassOf(typeof(UnityEventBase))) {
                        //Debug.Log("EVENT");
                        isEvent = true;
                    }
                }

                AddtoClass(new Node(v.Identifier.ToString(), isEvent ? NodeTypes.Event : NodeTypes.Field, v, mods.Public, mods.Static));
            }

            base.VisitFieldDeclaration(node);
        }

        //====================================================================================================//
        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node) {
            var mods = GetModifiers(node.Modifiers);
            AddtoClass(new Node(node.Identifier.ToString(), NodeTypes.Property, node, mods.Public, mods.Static));
            base.VisitPropertyDeclaration(node);
        }

        //====================================================================================================//
        void AddtoClass(Node node) {
            var parent = node.SyntaxNode.FirstAncestorOrSelf<ClassDeclarationSyntax>();
            foreach (var n in Nodes) {
                if (n.SyntaxNode == parent) {
                    n.Nodes.Add(node);
                    return;
                }
            }
        }

        //====================================================================================================//
        public Type GetTypeByName(string name) {
            /*if(Types.Contains(name)) {
                Debug.Log("Found: " + name);

                return null;
            }*/

            return null;
        }

        //====================================================================================================//
        (bool Public, bool Static) GetModifiers(SyntaxTokenList modifiers) {
            List<string> m = new List<string>();
            m = modifiers.Select(m => m.ToString()).ToList();

            return (m.Contains("public"), m.Contains("static"));
        }

        //====================================================================================================//
        public override void VisitToken(SyntaxToken token) {
            //Debug.Log("Token: " + token.Text);

            base.VisitLeadingTrivia(token);

            var isProcessed = false;

            var info = Model.GetSymbolInfo(token.Parent);
            if (CodeBrowser.HoverNode?.SyntaxNode != null && token.Parent.AncestorsAndSelf().Contains(CodeBrowser.HoverNode.SyntaxNode)) {
                WriteCode(TokenKind.NamedType, token.Text);
            }


            if (token.IsKeyword()) {
                WriteCode(TokenKind.Keyword, token.Text);
                isProcessed = true;
            }
            else {
                switch (token.Kind()) {
                    case SyntaxKind.StringLiteralToken:
                        WriteCode(TokenKind.StringLiteral, token.Text);
                        isProcessed = true;
                        break;
                    case SyntaxKind.CharacterLiteralToken:
                        WriteCode(TokenKind.CharacterLiteral, token.Text);
                        isProcessed = true;
                        break;
                    case SyntaxKind.IdentifierToken:
                        if (token.Parent is SimpleNameSyntax) {
                            // SimpleName is the base type of IdentifierNameSyntax, GenericNameSyntax etc.
                            // This handles type names that appear in variable declarations etc.
                            // e.g. "TypeName x = a + b;"
                            var name = (SimpleNameSyntax)token.Parent;
                            var semanticInfo = Model.GetSymbolInfo(name);
                            if (semanticInfo.Symbol != null && semanticInfo.Symbol.Kind != SymbolKind.ErrorType) {
                                switch (semanticInfo.Symbol.Kind) {
                                    case SymbolKind.NamedType:
                                        WriteCode(TokenKind.NamedType, token.Text);
                                        isProcessed = true;
                                        break;
                                    case SymbolKind.Namespace:
                                        WriteCode(TokenKind.Namespace, token.Text);
                                        isProcessed = true;
                                        break;
                                    case SymbolKind.Parameter:
                                        WriteCode(TokenKind.Parameter, token.Text);
                                        isProcessed = true;
                                        break;
                                    case SymbolKind.Local:
                                        WriteCode(TokenKind.Local, token.Text);
                                        isProcessed = true;
                                        break;
                                    case SymbolKind.Field:
                                        WriteCode(TokenKind.Field, token.Text);
                                        isProcessed = true;
                                        break;
                                    case SymbolKind.Property:
                                        WriteCode(TokenKind.Property, token.Text);
                                        isProcessed = true;
                                        break;
                                    default:
                                        break;
                                }
                            }
                        }
                        else if (token.Parent is TypeDeclarationSyntax) {
                            // TypeDeclarationSyntax is the base type of ClassDeclarationSyntax etc.
                            // This handles type names that appear in type declarations
                            // e.g. "class TypeName { }"
                            var name = (TypeDeclarationSyntax)token.Parent;
                            var symbol = Model.GetDeclaredSymbol(name);
                            if (symbol != null && symbol.Kind != SymbolKind.ErrorType) {
                                switch (symbol.Kind) {
                                    case SymbolKind.NamedType:
                                        WriteCode(TokenKind.NamedType, token.Text);
                                        isProcessed = true;
                                        break;
                                }
                            }
                        }
                        break;
                }
            }

            if (!isProcessed)
                HandleSpecialCaseIdentifiers(token);

            base.VisitTrailingTrivia(token);
            base.VisitToken(token);
        }

        //====================================================================================================//
        public override void VisitTrivia(SyntaxTrivia trivia) {
            //Debug.Log("Trivia: " + trivia.ToFullString());

            switch (trivia.Kind()) {
                case SyntaxKind.MultiLineCommentTrivia:
                case SyntaxKind.SingleLineCommentTrivia:
                    WriteCode(TokenKind.Comment, trivia.ToFullString());
                    break;
                case SyntaxKind.DisabledTextTrivia:
                    WriteCode(TokenKind.DisabledText, trivia.ToFullString());
                    break;
                case SyntaxKind.DocumentationCommentExteriorTrivia:
                    WriteCode(TokenKind.Comment, trivia.ToFullString());
                    break;
                case SyntaxKind.RegionDirectiveTrivia:
                case SyntaxKind.EndRegionDirectiveTrivia:
                    WriteCode(TokenKind.Region, trivia.ToFullString());
                    break;
                default:
                    WriteCode(TokenKind.None, trivia.ToFullString());
                    break;
            }
            base.VisitTrivia(trivia);
        }

        //====================================================================================================//
        public Dictionary<TokenKind, string> Colors = new Dictionary<TokenKind, string>() {
        { TokenKind.Keyword, "#7FF" },
        { TokenKind.Identifier, "#F7F" },
        { TokenKind.StringLiteral, "#77F" },
        { TokenKind.CharacterLiteral, "#77F" },
        { TokenKind.Comment, "#7F7" },
        { TokenKind.DisabledText, "#777" },
        { TokenKind.Region, "#F77" },
        { TokenKind.NamedType, "#FF7" },
        { TokenKind.Namespace, "#FF7" },
        { TokenKind.Parameter, "#F77" },
    };

        //====================================================================================================//
        public void WriteCode(TokenKind kind, string text) {
            if (kind == TokenKind.None)
                Text.Append(text);
            else {
                string color = "#777";
                Colors.TryGetValue(kind, out color);
                Text.Append("<color=" + color + ">" + text + "</color>");
            }
        }

        //====================================================================================================//
        void HandleSpecialCaseIdentifiers(SyntaxToken token) {
            bool ident = token.Parent?.Kind() == SyntaxKind.IdentifierName;
            var pKind = token.Parent?.Kind();
            var ppKind = token.Parent?.Parent?.Kind();
            var pppKind = token.Parent?.Parent?.Parent?.Kind();

            if (pKind == null || ppKind == null | pppKind == null) {
                WriteCode(TokenKind.None, token.Text);
                return;
            }

            switch (token.Kind()) {
                case SyntaxKind.IdentifierToken:
                    if ((ident && ppKind == SyntaxKind.Parameter)
                      || (pKind == SyntaxKind.EnumDeclaration)
                      || (ident && ppKind == SyntaxKind.Attribute)
                      || (ident && ppKind == SyntaxKind.CatchDeclaration)
                      || (ident && ppKind == SyntaxKind.ObjectCreationExpression)
                      || (ident && ppKind == SyntaxKind.ForEachStatement && !(token.GetNextToken().Kind() == SyntaxKind.CloseParenToken))
                      || (ident && pppKind == SyntaxKind.CaseSwitchLabel && !(token.GetPreviousToken().Kind() == SyntaxKind.DotToken))
                      || (ident && ppKind == SyntaxKind.MethodDeclaration)
                      || (ident && ppKind == SyntaxKind.CastExpression)
                      || (pKind == SyntaxKind.GenericName && ppKind == SyntaxKind.VariableDeclaration)
                      || (pKind == SyntaxKind.GenericName && ppKind == SyntaxKind.ObjectCreationExpression)
                      || (ident && ppKind == SyntaxKind.BaseList)
                      || (ident && pppKind == SyntaxKind.TypeOfExpression)
                      || (ident && ppKind == SyntaxKind.VariableDeclaration)
                      || (ident && ppKind == SyntaxKind.TypeArgumentList)
                      || (ident && ppKind == SyntaxKind.SimpleMemberAccessExpression && pppKind == SyntaxKind.Argument && !(token.GetPreviousToken().Kind() == SyntaxKind.DotToken || Char.IsLower(token.Text[0])))
                      || (ident && ppKind == SyntaxKind.SimpleMemberAccessExpression && !(token.GetPreviousToken().Kind() == SyntaxKind.DotToken || Char.IsLower(token.Text[0])))
                      ) {
                        WriteCode(TokenKind.Identifier, token.Text);
                    }
                    else
                        WriteCode(TokenKind.None, token.Text);

                    break;
                default:
                    WriteCode(TokenKind.None, token.Text);
                    break;
            }
        }
    }





    public enum NodeTypes { Class, Method, Property, Field, Event, Type };
    public class Node {
        public string Name;
        public NodeTypes Type;
        public string DataType;
        public bool isPublic = true;
        public bool isStatic;
        public SyntaxNode SyntaxNode;
        public int StartLine;
        public int EndLine;
        public string Code;
        public List<Node> Nodes = new List<Node>();

        public Node(string name, NodeTypes type, SyntaxNode node, bool pub = true, bool stat = false) {
            Name = name;
            Type = type;
            SyntaxNode = node;
            StartLine = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            EndLine = node.GetLocation().GetLineSpan().EndLinePosition.Line + 1;
            Code = node.ToString();
            isPublic = pub;
            isStatic = stat;
        }
    }
}
