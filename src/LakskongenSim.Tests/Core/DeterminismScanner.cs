using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LakskongenSim.Tests.Core;

/// <summary>
/// One banned construct found in a source file: the file, the construct's
/// label and the symbol (enclosing type chain and member) it sits in.
/// </summary>
public sealed record DeterminismFinding(string Path, string Pattern, string Symbol)
{
    /// <summary>The allowlist key: path, pattern and symbol (no line text).</summary>
    public string Key => $"{Path} :: {Pattern} :: {Symbol}";
}

/// <summary>
/// Syntax-aware scan for unseeded randomness, wall clocks and process-random
/// hashes (P0-07, review RECHECK §2 lows). The old scan matched a regex per
/// line, so a reformatted or renamed line broke the allowlist key, comments
/// and strings counted as code, and DateTimeOffset, Environment.TickCount and
/// RandomNumberGenerator were not banned at all. This one walks the Roslyn
/// syntax tree (comments and string literals are not code; interpolation
/// holes are) and keys each finding by the symbol that contains it.
/// </summary>
public static class DeterminismScanner
{
    /// <summary>
    /// F2-04 (docs/FARM_TYPES_SPEC.md §3.7): bridge files scanned with the sim
    /// library. The farm-type lanes' bridge partials hold verbs that start
    /// builds, file permit cases and book operations, so they must draw nothing
    /// unseeded either; the allowlist holds no entry for them.
    /// </summary>
    public static readonly IReadOnlyList<string> BridgeScope = new[]
    {
        "godot/scripts/autoload/GameBridge.LandSites.cs",
        "godot/scripts/autoload/GameBridge.Offshore.cs",
        "godot/scripts/autoload/GameBridge.FarmTypes.cs",
        "godot/scripts/autoload/GameBridge.Driftsplan.cs",
        // F2-31: the build flow's previews, plots and diagnosis (reads the build dialog and its ghost call).
        "godot/scripts/autoload/GameBridge.BuildFlow.cs",
        // Geo stages 2-3 track A: the geo lane's reads of the real coast and the rival layout.
        "godot/scripts/autoload/GameBridge.Geo.cs",
    };

    /// <summary>
    /// Banned static members as (type, member). "*" bans every member of the
    /// type. The label is "Type.Member" (or the type alone for "*").
    /// </summary>
    private static readonly (string Type, string Member)[] BannedMembers =
    {
        ("DateTime", "Now"),
        ("DateTime", "UtcNow"),
        ("DateTime", "Today"),
        ("DateTimeOffset", "Now"),
        ("DateTimeOffset", "UtcNow"),
        ("Environment", "TickCount"),
        ("Environment", "TickCount64"),
        ("Random", "Shared"),
        ("Guid", "NewGuid"),
        ("Guid", "CreateVersion7"),
        ("HashCode", "Combine"),
        ("RandomNumberGenerator", "*"),
    };

    /// <summary>Types whose members a <c>using static</c> would bring in unqualified.</summary>
    private static readonly HashSet<string> BannedStaticImports = new(StringComparer.Ordinal)
    {
        "DateTime", "DateTimeOffset", "Environment", "Guid", "HashCode", "RandomNumberGenerator",
    };

    /// <summary>Every banned construct in one C# source text.</summary>
    public static IReadOnlyList<DeterminismFinding> Scan(string path, string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var findings = new List<DeterminismFinding>();
        foreach (var node in root.DescendantNodes())
        {
            string? pattern = Classify(node);
            if (pattern != null)
                findings.Add(new DeterminismFinding(path, pattern, SymbolOf(node)));
        }
        return findings;
    }

    /// <summary>The banned-construct label of a node, or null when it is allowed.</summary>
    private static string? Classify(SyntaxNode node)
    {
        switch (node)
        {
            case MemberAccessExpressionSyntax access:
            {
                string type = StripSystem(access.Expression.ToString());
                string member = access.Name.Identifier.Text;
                foreach (var (bannedType, bannedMember) in BannedMembers)
                {
                    if (type != bannedType) continue;
                    if (bannedMember == "*") return bannedType;
                    if (bannedMember == member)
                        return access.Parent is InvocationExpressionSyntax
                            ? $"{bannedType}.{bannedMember}()"
                            : $"{bannedType}.{bannedMember}";
                }
                return null;
            }
            case InvocationExpressionSyntax call when call.ArgumentList.Arguments.Count == 0:
            {
                // Process-random hashing: string/object GetHashCode() (qualified or not).
                string? name = call.Expression switch
                {
                    MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
                    IdentifierNameSyntax id => id.Identifier.Text,
                    _ => null
                };
                return name == "GetHashCode" ? ".GetHashCode()" : null;
            }
            case ObjectCreationExpressionSyntax create
                when StripSystem(create.Type.ToString()) == "Random"
                     && (create.ArgumentList == null || create.ArgumentList.Arguments.Count == 0):
                return "new Random()";
            case ImplicitObjectCreationExpressionSyntax implicitNew
                when implicitNew.ArgumentList.Arguments.Count == 0 && TargetTypeOf(implicitNew) == "Random":
                return "new Random()";
            case UsingDirectiveSyntax import
                when import.StaticKeyword.IsKind(SyntaxKind.StaticKeyword)
                     && import.Name != null
                     && BannedStaticImports.Contains(StripSystem(import.Name.ToString())):
                return $"using static {StripSystem(import.Name.ToString())}";
            default:
                return null;
        }
    }

    /// <summary>"global::System.DateTime" and "System.DateTime" are "DateTime".</summary>
    private static string StripSystem(string text)
    {
        string t = string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
        if (t.StartsWith("global::", StringComparison.Ordinal)) t = t["global::".Length..];
        if (t.StartsWith("System.Security.Cryptography.", StringComparison.Ordinal))
            t = t["System.Security.Cryptography.".Length..];
        if (t.StartsWith("System.", StringComparison.Ordinal)) t = t["System.".Length..];
        return t;
    }

    /// <summary>Declared type a target-typed <c>new()</c> creates (field, local or property).</summary>
    private static string? TargetTypeOf(ImplicitObjectCreationExpressionSyntax node)
    {
        var declaredType = node.Parent?.Parent switch
        {
            VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } => declaration.Type,
            PropertyDeclarationSyntax property => property.Type,
            _ => null
        };
        return declaredType == null ? null : StripSystem(declaredType.ToString());
    }

    /// <summary>
    /// The enclosing symbol: nested type names, then the member ("Type.Member").
    /// Field initializers belong to their field, accessors and expression
    /// bodies to their property, lambdas and local functions to the member
    /// that declares them, constructors are ".ctor" (static ".cctor").
    /// </summary>
    public static string SymbolOf(SyntaxNode node)
    {
        string? member = null;
        var types = new List<string>();
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            if (member == null)
                member = MemberName(ancestor, node);
            if (ancestor is BaseTypeDeclarationSyntax type)
                types.Add(type.Identifier.Text);
            else if (ancestor is DelegateDeclarationSyntax del)
                types.Add(del.Identifier.Text);
        }
        types.Reverse();
        string typeChain = types.Count > 0 ? string.Join(".", types) : "<file>";
        return member == null ? typeChain : $"{typeChain}.{member}";
    }

    private static string? MemberName(SyntaxNode ancestor, SyntaxNode origin) => ancestor switch
    {
        MethodDeclarationSyntax m => m.Identifier.Text,
        ConstructorDeclarationSyntax c => c.Modifiers.Any(SyntaxKind.StaticKeyword) ? ".cctor" : ".ctor",
        DestructorDeclarationSyntax => "Finalize",
        OperatorDeclarationSyntax op => "op_" + op.OperatorToken.Text,
        ConversionOperatorDeclarationSyntax conv => "op_" + conv.ImplicitOrExplicitKeyword.Text,
        PropertyDeclarationSyntax p => p.Identifier.Text,
        IndexerDeclarationSyntax => "this[]",
        EventDeclarationSyntax e => e.Identifier.Text,
        EnumMemberDeclarationSyntax em => em.Identifier.Text,
        BaseFieldDeclarationSyntax field => field.Declaration.Variables
            .FirstOrDefault(v => v.Span.Contains(origin.Span))?.Identifier.Text
            ?? string.Join(",", field.Declaration.Variables.Select(v => v.Identifier.Text)),
        _ => null
    };
}
