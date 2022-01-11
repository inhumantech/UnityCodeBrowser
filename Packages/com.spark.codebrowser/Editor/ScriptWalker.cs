using System;
using System.Text;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.Events;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UnityEngine;

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
    public ScriptWalker(UnityEngine.Object obj, SemanticModel model) : base(SyntaxWalkerDepth.Node) {
        Module = obj;
        Model = model;
        Text = new StringBuilder();
        Type type = obj.GetType();

        foreach (var item in type.GetMembers()) {
            if (item is FieldInfo) {
                string n = (item as FieldInfo).FieldType.AssemblyQualifiedName;
                Type t = Type.GetType(n);

                if (t != null && !Types.ContainsKey(t.Name))
                    Types[t.Name] = t;
            }
            else if (item is PropertyInfo) {
                string n = (item as PropertyInfo).PropertyType.AssemblyQualifiedName;
                Type t = Type.GetType(n);

                if (t != null && !Types.ContainsKey(t.Name))
                    Types[t.Name] = t;
            }
        }
    }

    //====================================================================================================//
    public override void VisitUsingDirective(UsingDirectiveSyntax node) {
        Usings.Add(node.Name.ToString());
        base.VisitUsingDirective(node);
    }

    //====================================================================================================//
    public override void VisitClassDeclaration(ClassDeclarationSyntax node) {
        Nodes.Add(new Node(node.Identifier.ToString(), NodeTypes.Class, node));
        base.VisitClassDeclaration(node);
    }

    //====================================================================================================//
    public override void VisitMethodDeclaration(MethodDeclarationSyntax node) {
        string docs = GetDocs(node);
        var mods = GetModifiers(node.Modifiers);
        AddtoClass(new Node(node.Identifier.ToString(), NodeTypes.Method, node, docs, mods.Public, mods.Static));
        base.VisitMethodDeclaration(node);
    }

    //====================================================================================================//
    string GetDocs(SyntaxNode node) {
        List<string> lines = new List<string>();
        foreach (var trivia in node.GetLeadingTrivia()) {
            if (!trivia.HasStructure)
                continue;

            var structure = trivia.GetStructure();
            foreach (var n in structure.ChildNodes()) {
                if (n.Kind() == SyntaxKind.XmlElement) {
                    var xml = (XmlElementSyntax)n;
                    lines.Add("<b><color=cyan>" + xml.StartTag.Name + "</color></b>: " + xml.Content.ToString().Replace("///", "").Trim());
                }
            }
        }

        return string.Join("\n", lines);
    }

    //====================================================================================================//
    public override void VisitFieldDeclaration(FieldDeclarationSyntax node) {
        foreach (var v in node.Declaration.Variables) {
            string docs = GetDocs(v);
            var mods = GetModifiers(node.Modifiers);
            string typeName = node.Declaration.Type.ToString();
            bool isEvent = false;

            if (Types.ContainsKey(typeName) && Types[typeName].IsSubclassOf(typeof(UnityEventBase)))
                isEvent = true;

            AddtoClass(new Node(v.Identifier.ToString(), isEvent ? NodeTypes.Event : NodeTypes.Field, v, docs, mods.Public, mods.Static));
        }

        base.VisitFieldDeclaration(node);
    }

    //====================================================================================================//
    public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node) {
        string docs = GetDocs(node);
        var mods = GetModifiers(node.Modifiers);
        AddtoClass(new Node(node.Identifier.ToString(), NodeTypes.Property, node, docs, mods.Public, mods.Static));
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
    (bool Public, bool Static) GetModifiers(SyntaxTokenList modifiers) {
        List<string> m = new List<string>();
        m = modifiers.Select(m => m.ToString()).ToList();

        return (m.Contains("public"), m.Contains("static"));
    }

    //====================================================================================================//
    public override void VisitToken(SyntaxToken token) {
        base.VisitLeadingTrivia(token);
        var isProcessed = false;

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
        string text = trivia.ToFullString();

        switch (trivia.Kind()) {
            case SyntaxKind.MultiLineCommentTrivia:
            case SyntaxKind.SingleLineCommentTrivia:
                WriteCode(TokenKind.Comment, text);
                break;
            case SyntaxKind.DisabledTextTrivia:
                WriteCode(TokenKind.DisabledText, text);
                break;
            case SyntaxKind.DocumentationCommentExteriorTrivia:
                WriteCode(TokenKind.Comment, text);
                break;
            case SyntaxKind.RegionDirectiveTrivia:
            case SyntaxKind.EndRegionDirectiveTrivia:
                WriteCode(TokenKind.Region, text);
                break;
            default:
                WriteCode(TokenKind.None, text);
                break;
        }

        base.VisitTrivia(trivia);
    }

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
    public string Docs;
    public List<Node> Nodes = new List<Node>();

    public Node(string name, NodeTypes type, SyntaxNode node, string docs = "", bool pub = true, bool stat = false) {
        Name = name;
        Type = type;
        SyntaxNode = node;
        StartLine = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        EndLine = node.GetLocation().GetLineSpan().EndLinePosition.Line + 1;
        Code = node.ToString();
        Docs = docs;
        isPublic = pub;
        isStatic = stat;
    }
}

