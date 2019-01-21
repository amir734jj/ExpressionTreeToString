# ExpressionToSyntaxNode
Maps expression trees to Roslyn Syntax Node trees, using the SyntaxNode API for cross-language support

This is inspired by the excellent [ReadableExpressions](https://github.com/agileobjects/ReadableExpressions) project. However, it has the following limitations:

1. code output is only in C#
2. closed-over variables in VB.NET expressions are not handled

The goals of this project are:

1. Provide an extension method on expressions that returns a Roslyn syntax tree, in either C# or VB.NET, using the language-independent `SyntaxNode` API as much as possible, and dropping down to the language-specific `SyntaxFactory` as needed.
2. Provide an extension method on expressions that returns a string representation of that tree, with optional trivia insertion.
3. Provide a visualizer with a tree representation of the expression, alongside the code
    * Ideally the trivia insertion within the visualizer will be based on project settings, or the user's IDE settings, if possible

Note that expression support is incomplete -- lots of `NotImplementedException`.

Screenshot of the visualizer -- VB output, and highlighting a parameter and closed-over variables

![Screenshot](screenshot-vb.jpg)

Slightly more complicated expression -- C# output and some constants, in addition to the same closed-over variables:

![Screenshot](screenshot-csharp.jpg)
