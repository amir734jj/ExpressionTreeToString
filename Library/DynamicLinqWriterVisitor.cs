﻿using ExpressionTreeToString.Util;
using OneOf;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using ZSpitz.Util;
using static System.Linq.Expressions.ExpressionType;
using static ZSpitz.Util.Functions;
using static System.Linq.Enumerable;
using static ExpressionTreeToString.Util.Functions;

namespace ExpressionTreeToString {
    public class DynamicLinqWriterVisitor : BuiltinsWriterVisitor {
        public static readonly HashSet<Type> CustomAccessibleTypes = new HashSet<Type>();
        private static readonly HashSet<Type> predefinedTypes = new HashSet<Type> {
            typeof(object),
            typeof(bool),
            typeof(char),
            typeof(string),
            typeof(sbyte),
            typeof(byte),
            typeof(short),
            typeof(ushort),
            typeof(int),
            typeof(uint),
            typeof(long),
            typeof(ulong),
            typeof(float),
            typeof(double),
            typeof(decimal),
            typeof(DateTime),
            typeof(DateTimeOffset),
            typeof(TimeSpan),
            typeof(Guid),
            typeof(Math),
            typeof(Convert),
            typeof(Uri)
        };

        private static bool isAccessibleType(Type t) => 
            t.IsNullable() ? 
                isAccessibleType(t.UnderlyingSystemType) : 
                t.In(predefinedTypes) || t.In(CustomAccessibleTypes);

        ParameterExpression? currentScoped;

        public DynamicLinqWriterVisitor(object o, OneOf<string, Language?> languageArg, bool hasPathSpans) :
            base(o, languageArg, null, hasPathSpans) { }

        private static readonly Dictionary<ExpressionType, string> simpleBinaryOperators = new Dictionary<ExpressionType, string>() {
            [Add] = "+",
            [AddChecked] = "+",
            [Divide] = "/",
            [Modulo] = "%",
            [Multiply] = "*",
            [MultiplyChecked] = "*",
            [Subtract] = "-",
            [SubtractChecked] = "-",
            [AndAlso] = "&&",
            [OrElse] = "||",
            [Equal] = "==",
            [NotEqual] = "!=",
            [GreaterThanOrEqual] = ">=",
            [GreaterThan] = ">",
            [LessThan] = "<",
            [LessThanOrEqual] = "<=",
            [Coalesce] = "??",
        };

        // TODO parentheses, for preferred order of operations

        private bool isEquivalent(Expression x, Expression y) {
            // TODO we need to handle a lot more here -- e.g. method calls, constants
            return isMemberChainEqual(x, y);
        }

        protected override void WriteBinary(BinaryExpression expr) {
            var values = new List<Expression>();
            var grouped = expr.OrClauses().ToLookup(orClause => {
                if (!(orClause.clause is BinaryExpression bexpr && bexpr.NodeType == Equal)) { return null; }
                var matched = values.FirstOrDefault(x => isEquivalent(x, bexpr.Left));
                if (matched is null) {
                    matched = bexpr.Left;
                    values.Add(matched);
                }
                return matched;
            });

            (Expression left, string leftPath, Expression right, string rightPath) parts;

            if (grouped.Any(grp => grp.Key is { } && grp.Count()>1)) { // can any elements be written using `in`?
                var firstClause = true;
                foreach (var grp in grouped) {
                    if (firstClause) {
                        firstClause = false;
                    } else {
                        Write(" || ");
                    }

                    if (grp.Key is null || grp.Count() == 1) {
                        // if only one element in the group, there's no need for a foreach
                        // but the rest of the logic is shared
                        foreach (var x in grp) {
                            WriteNode(x);
                        }
                        continue;
                    }

                    // write value
                    var firstElement = true;
                    foreach (var (path, clause) in grp) {
                        var bexpr = (BinaryExpression)clause;
                        var (left, leftPath, right, rightPath) = (
                            bexpr.Left,
                            "Left",
                            bexpr.Right,
                            "Right"
                        );
                        if (TryGetEnumComparison(clause, out parts)) {
                            (left, leftPath, right, rightPath) = parts;
                        }

                        if (firstElement) {
                            WriteNode($"{path}.{leftPath}", left);
                            Write(" in (");
                            firstElement = false;
                        } else {
                            Write(", ");
                        }
                        WriteNode($"{path}.{rightPath}", right);
                    }
                    Write(")");
                }
                return;
            }

            if (TryGetEnumComparison(expr, out parts)) {
                var (left, leftPath, right, rightPath) = parts;
                WriteNode(leftPath, left);
                Write($" {simpleBinaryOperators[expr.NodeType]} ");
                WriteNode(rightPath, right);
                return;
            }

            if (simpleBinaryOperators.TryGetValue(expr.NodeType, out var @operator)) {
                WriteNode("Left", expr.Left);
                Write($" {@operator} ");
                WriteNode("Right", expr.Right);
                return;
            }

            if (expr.NodeType == ArrayIndex) {
                WriteNode("Left", expr.Left);
                Write("[");
                WriteNode("Right", expr.Right);
                Write("]");
                return;
            }

            throw new NotImplementedException();
        }

        protected override void WriteUnary(UnaryExpression expr) {
            switch (expr.NodeType) {
                case ExpressionType.Convert:
                case ConvertChecked:
                case Unbox:
                    var renderConversion = 
                        expr.Type != expr.Operand.Type && 
                        !expr.Operand.Type.HasImplicitConversionTo(expr.Type);
                    if (renderConversion) { Write($"{typeName(expr.Type)}("); }
                    WriteNode("Operand", expr.Operand);
                    if (renderConversion) { Write(")"); }
                    break;
                case Not:
                    Write("-");
                    WriteNode("Operand", expr.Operand);
                    break;
                case Negate:
                case NegateChecked:
                    if (expr.Type.UnderlyingIfNullable() == typeof(bool)) {
                        Write("!");
                    } else {
                        Write("-");
                    }
                    WriteNode("Operand", expr.Operand);
                    break;
                case TypeAs :
                    if (expr.Operand != currentScoped) {
                        throw new NotImplementedException("'as' only supported on ParameterExpression in current scope.");
                    }
                    Write($"as(\"{expr.Type.FullName}\")");
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        protected override void WriteLambda(LambdaExpression expr) {
            var count = expr.Parameters.Count;
            if (count > 1) {
                throw new NotImplementedException("Multiple parameters in lambda expression.");
            } else if (count == 1) {
                currentScoped = expr.Parameters[0];
            }
            WriteNode("Body", expr.Body);
        }

        protected override void WriteParameter(ParameterExpression expr) {
            if (currentScoped is { } && expr != currentScoped) {
                throw new NotImplementedException("Multiple ParameterExpression in current scope.");
            }

            // if we got here, that means we need to write out the ParameterExpression
            Write("it");
        }

        protected override void WriteConstant(ConstantExpression expr) {
            var value = expr.Value;
            var underlying = value?.GetType().UnderlyingIfNullable() ?? typeof(void);
            if (
                value is null ||
                value is bool ||
                value is string ||
                value is char ||
                underlying.IsEnum ||
                underlying.IsNumeric()
            ) {
                Write(RenderLiteral(value, "C#"));
                return;
            }

            if (value is DateTime dte) {
                // we need to supply both parameters to the DateTime constructor;
                // otherwise Dynamic LINQ interperts it as a conversion, and fails
                Write($"DateTime({dte.Ticks}, DateTimeKind.{dte.Kind})");
                return;
            }

            // TODO handle DateTimeOffset and TimeSpan

            throw new NotImplementedException();
        }

        protected override void WriteMemberAccess(MemberExpression expr) =>
            writeMemberUse("Expression", expr.Expression, expr.Member);

        protected override void WriteNew(NewExpression expr) {
            if (expr.Type.IsAnonymous()) {
                Write("new(");
                expr.Constructor.GetParameters().Select(x => x.Name).Zip(expr.Arguments).ForEachT((name, arg, index) => {
                    if (index > 0) { Write(", "); }

                    // if the expression being assigned from is a property access with the same name as the target property, 
                    // write only the target expression.
                    // Otherwise, write `property = expression`
                    if (!(arg is MemberExpression mexpr && mexpr.Member.Name.Replace("$VB$Local_", "") == name)) {
                        Write($"{name} = ");
                    }
                    WriteNode($"Arguments[{index}]", arg);
                });
                Write(")");
                return;
            }

            Write(typeName(expr.Type));
            Write("(");
            WriteNodes("Arguments", expr.Arguments);
            Write(")");
        }

        private void writeIndexerAccess(string instancePath, Expression instance, string argumentsPath, IEnumerable<Expression> arguments) {
            var lst = arguments.ToList();
            if (instance.Type.IsArray && lst.Count > 1) {
                throw new NotImplementedException("Multidimensional array access not supported.");
            }
            // No such thing as a static indexer
            WriteNode(instancePath, instance);
            Write("[");
            WriteNodes(argumentsPath, lst);
            Write("]");
        }

        private void writeMemberUse(string instancePath, Expression? instance, MemberInfo mi) {
            var declaringType = mi.DeclaringType;
            if (instance is null) {
                if (!isAccessibleType(declaringType)) {
                    throw new NotImplementedException($"Type '{declaringType.Name}' is not an accessible type; its' static methods cannot be used.");
                }
                Write(typeName(declaringType) + ".");
            } else {
                if (instance.Type.IsClosureClass()) {
                    throw new NotImplementedException("No representation for closed-over variables.");
                } else if (mi is MethodInfo mthd && !(isAccessibleType(declaringType) || isAccessibleType(mthd.ReturnType))) {
                    throw new NotImplementedException("Instance methods must either be on an accessible type, or return an instance of an accessible type.");
                } else if (instance.SansConvert() != currentScoped) {
                    WriteNode(instancePath, instance);
                    Write(".");
                }
            }
            Write(mi.Name);
        }

        private static readonly MethodInfo[] containsMethods = IIFE(() => {
            IEnumerable<char> e = "";
            var q = "".AsQueryable();

            return new[] {
                GetMethod(() => e.Contains('a')),
                GetMethod(() => q.Contains('a'))
            };
        });

        private static readonly MethodInfo[] sequenceMethods = IIFE(() => {
            IEnumerable<char> e = "";
            var ordered = e.OrderBy(x => x);

            var q = "".AsQueryable();
            var orderedQ = q.OrderBy(x => x);

            // TODO what about Max/Min/Sum without predicate?

            return new[] {
                #region Enumerable
                GetMethod(() => e.All(x => true)),
                GetMethod(() => e.Any()),
                GetMethod(() => e.Any(x => true)),
                GetMethod(() => e.Average(x => x)),
                GetMethod(() => e.Cast<char>()),
                GetMethod(() => e.Contains('a')),
                GetMethod(() => e.Count()),
                GetMethod(() => e.Count(x => true)),
                GetMethod(() => e.DefaultIfEmpty()),
                GetMethod(() => e.DefaultIfEmpty('a')),
                GetMethod(() => e.Distinct()),
                GetMethod(() => e.First()),
                GetMethod(() => e.First(x => true)),
                GetMethod(() => e.FirstOrDefault()),
                GetMethod(() => e.FirstOrDefault(x => true)),
                GetMethod(() => e.GroupBy(x => x)),
                GetMethod(() => e.GroupBy(x => x, x => x)),
                GetMethod(() => e.Last()),
                GetMethod(() => e.Last(x => true)),
                GetMethod(() => e.LongCount()),
                GetMethod(() => e.LongCount(x => true)),
                GetMethod(() => e.Max(x => true)),
                GetMethod(() => e.Min(x => true)),
                GetMethod(() => e.OfType<char>()),
                GetMethod(() => e.OrderBy(x => x)),
                GetMethod(() => e.OrderByDescending(x=>x)),
                GetMethod(() => e.Select(x => x)),
                GetMethod(() => e.SelectMany(x => new int[] { })),
                GetMethod(() => e.Single()),
                GetMethod(() => e.Single(x => true)),
                GetMethod(() => e.SingleOrDefault()),
                GetMethod(() => e.SingleOrDefault(x => true)),
                GetMethod(() => e.Skip(1)),
                GetMethod(() => e.SkipWhile(x => true)),
                GetMethod(() => e.Sum(x => x)),
                GetMethod(() => e.Take(1)),
                GetMethod(() => e.TakeWhile(x => true)),
                GetMethod(() => ordered.ThenBy(x => x)),
                GetMethod(() => ordered.ThenByDescending(x => x)),
                GetMethod(() => e.Where(x => true)),
                #endregion

                #region Queryable
                GetMethod(() => q.All(x => true)),
                GetMethod(() => q.Any()),
                GetMethod(() => q.Any(x => true)),
                GetMethod(() => q.Average(x => x)),
                GetMethod(() => q.Cast<char>()),
                GetMethod(() => q.Contains('a')),
                GetMethod(() => q.Count()),
                GetMethod(() => q.Count(x => true)),
                GetMethod(() => q.DefaultIfEmpty()),
                GetMethod(() => q.DefaultIfEmpty('a')),
                GetMethod(() => q.Distinct()),
                GetMethod(() => q.First()),
                GetMethod(() => q.First(x => true)),
                GetMethod(() => q.FirstOrDefault()),
                GetMethod(() => q.FirstOrDefault(x => true)),
                GetMethod(() => q.GroupBy(x => x)),
                GetMethod(() => q.GroupBy(x => x, x => x)),
                GetMethod(() => q.Last()),
                GetMethod(() => q.Last(x => true)),
                GetMethod(() => q.LongCount()),
                GetMethod(() => q.LongCount(x => true)),
                GetMethod(() => q.Max(x => true)),
                GetMethod(() => q.Min(x => true)),
                GetMethod(() => q.OfType<char>()),
                GetMethod(() => q.OrderBy(x => x)),
                GetMethod(() => q.OrderByDescending(x=>x)),
                GetMethod(() => q.Select(x => x)),
                GetMethod(() => q.SelectMany(x => new int[] { })),
                GetMethod(() => q.Single()),
                GetMethod(() => q.Single(x => true)),
                GetMethod(() => q.SingleOrDefault()),
                GetMethod(() => q.SingleOrDefault(x => true)),
                GetMethod(() => q.Skip(1)),
                GetMethod(() => q.SkipWhile(x => true)),
                GetMethod(() => q.Sum(x => x)),
                GetMethod(() => q.Take(1)),
                GetMethod(() => q.TakeWhile(x => true)),
                GetMethod(() => orderedQ.ThenBy(x => x)),
                GetMethod(() => orderedQ.ThenByDescending(x => x)),
                GetMethod(() => q.Where(x => true))
                #endregion
            }.Select(x => x.GetGenericMethodDefinition()).ToArray();
        });

        protected override void WriteCall(MethodCallExpression expr) {
            if (expr.Method.IsStringConcat()) {
                var firstArg = expr.Arguments[0];
                IEnumerable<Expression>? argsToWrite = null;
                var argsPath = "";
                if (firstArg is NewArrayExpression newArray && firstArg.NodeType == NewArrayInit) {
                    argsToWrite = newArray.Expressions;
                    argsPath = "Arguments[0].Expressions";
                } else if (expr.Arguments.All(x => x.Type == typeof(string))) {
                    argsToWrite = expr.Arguments;
                    argsPath = "Arguments";
                }
                if (argsToWrite != null) {
                    WriteNodes(argsPath, argsToWrite, " + ");
                    return;
                }
            }

            if (expr.Method.In(containsMethods)) {
                WriteNode("Arguments[0]", expr.Arguments[0]);
                Write(" in ");
                WriteNode("Arguments[1]", expr.Arguments[1]);
                return;
            }

            var instance = expr.Object;
            var isIndexer = 
                instance is { } && instance.Type.IsArray && expr.Method.Name == "Get" || 
                expr.Method.IsIndexerMethod();
            if (isIndexer) {
                writeIndexerAccess("Object", expr.Object!, "Arguments", expr.Arguments);
                return;
            }

            var path = "Object";
            var skip = 0;

            if (expr.Method.IsGenericMethod && expr.Method.GetGenericMethodDefinition().In(sequenceMethods)) {
                // they're all static extension methods on IEnumerable<T> / IQueryable<T>, so no further tests required
                path = "Arguments[0]";
                instance = expr.Arguments[0];
                skip = 1;
            }

            var arguments = expr.Arguments.Skip(skip).Select((x, index) => ($"Arguments[{index + skip}]", x));

            writeMemberUse(path, instance, expr.Method);
            Write("(");
            WriteNodes(arguments);
            Write(")");
        }

        protected override void WriteMemberInit(MemberInitExpression expr) {
            throw new NotImplementedException();
        }

        protected override void WriteListInit(ListInitExpression expr) {
            throw new NotImplementedException();
        }

        protected override void WriteNewArray(NewArrayExpression expr) {
            throw new NotImplementedException();
        }

        private bool isMemberChainEqual(Expression x, Expression y) =>
            x.SansConvert() is MemberExpression mexpr1 && y.SansConvert() is MemberExpression mexpr2 ?
                    mexpr1.Member == mexpr2.Member && isMemberChainEqual(mexpr1.Expression, mexpr2.Expression) :
                    x == y;

        private bool doesTestMatchMember(Expression valueClause, Expression testClause) {
            if (!(valueClause is MemberExpression mexpr && testClause is BinaryExpression bexpr)) { return false; }
            if (bexpr.NodeType != NotEqual) { return false; }

            var (_, testExpression) = (bexpr.Left, bexpr.Right) switch
            {
                (ConstantExpression x, Expression y) when x.Value is null => (x, y),
                (Expression y, ConstantExpression x) when x.Value is null => (x, y),
                _ => (null, null)
            };
            return !(testExpression is null) && isMemberChainEqual(mexpr.Expression, testExpression);
        }

        protected override void WriteConditional(ConditionalExpression expr, object? metadata) {
            if (expr.Type == typeof(void)) {
                throw new NotImplementedException("Cannot represent void-returning conditionals.");
            };

            // TODO handle !(x.A == null) as well
            // TODO handle also !(x == null || x.A == null)

            // only check member expressions whose Expression.Type can take null (reference type or Nullable<T>)
            var memberClauses = expr.IfTrue.MemberClauses().Where(x => x.Expression.Type.IsNullable(true)).ToList();

            // we assume there are no test clauses for items in the member chain whose return value cannot be null
            var andClauses = expr.Test.AndClauses().ToList();
            if (
                memberClauses.Count > 0 &&
                memberClauses.Count == andClauses.Count &&
                memberClauses.Zip(andClauses).All(x => doesTestMatchMember(x.Item1, x.Item2))
            ) {
                Write("np(");
                WriteNode("IfTrue", expr.IfTrue);
                if (!(expr.IfFalse is ConstantExpression cexpr && cexpr.Value is null)) {
                    Write(", ");
                    WriteNode("IfFalse", expr.IfFalse);
                }
                Write(")");
                return;
            }

            Write("iif(");
            WriteNode("Test", expr.Test);
            Write(", ");
            WriteNode("IfTrue", expr.IfTrue);
            Write(", ");
            WriteNode("IfFalse", expr.IfFalse);
            Write(")");
        }

        protected override void WriteDefault(DefaultExpression expr) {
            throw new NotImplementedException();
        }

        protected override void WriteTypeBinary(TypeBinaryExpression expr) {
            if (expr.Expression != currentScoped) {
                throw new NotImplementedException("'is' only supported on ParameterExpression in current scope.");
            }
            Write($"is(\"{expr.Type.FullName}\")");
        }

        protected override void WriteInvocation(InvocationExpression expr) {
            throw new NotImplementedException("Pending https://github.com/zzzprojects/System.Linq.Dynamic.Core/issues/441");
            //Write("(");
            //WriteNode("Expression", expr.Expression);
            //Write(")(");
            //WriteNodes("Arguments", expr.Arguments);
            //Write(")");
        }

        protected override void WriteIndex(IndexExpression expr) =>
            writeIndexerAccess("Object", expr.Object, "Arguments", expr.Arguments);

        protected override void WriteBlock(BlockExpression expr, object? metadata) {
            throw new NotImplementedException();
        }

        protected override void WriteSwitch(SwitchExpression expr) {
            throw new NotImplementedException();
        }

        protected override void WriteTry(TryExpression expr) {
            throw new NotImplementedException();
        }

        protected override void WriteLabel(LabelExpression expr) {
            throw new NotImplementedException();
        }

        protected override void WriteGoto(GotoExpression expr) {
            throw new NotImplementedException();
        }

        protected override void WriteLoop(LoopExpression expr) {
            throw new NotImplementedException();
        }

        protected override void WriteRuntimeVariables(RuntimeVariablesExpression expr) {
            throw new NotImplementedException();
        }

        protected override void WriteDebugInfo(DebugInfoExpression expr) {
            throw new NotImplementedException();
        }

        protected override void WriteElementInit(ElementInit elementInit) {
            throw new NotImplementedException();
        }

        protected override void WriteBinding(MemberBinding binding) {
            throw new NotImplementedException();
        }

        protected override void WriteSwitchCase(SwitchCase switchCase) {
            throw new NotImplementedException();
        }

        protected override void WriteCatchBlock(CatchBlock catchBlock) {
            throw new NotImplementedException();
        }

        protected override void WriteLabelTarget(LabelTarget labelTarget) {
            throw new NotImplementedException();
        }

        protected override void WriteBinaryOperationBinder(BinaryOperationBinder binaryOperationBinder, IList<Expression> args) {
            throw new NotImplementedException();
        }

        protected override void WriteConvertBinder(ConvertBinder convertBinder, IList<Expression> args) {
            throw new NotImplementedException();
        }

        protected override void WriteCreateInstanceBinder(CreateInstanceBinder createInstanceBinder, IList<Expression> args) {
            throw new NotImplementedException();
        }

        protected override void WriteDeleteIndexBinder(DeleteIndexBinder deleteIndexBinder, IList<Expression> args) {
            throw new NotImplementedException();
        }

        protected override void WriteDeleteMemberBinder(DeleteMemberBinder deleteMemberBinder, IList<Expression> args) {
            throw new NotImplementedException();
        }

        protected override void WriteGetIndexBinder(GetIndexBinder getIndexBinder, IList<Expression> args) {
            throw new NotImplementedException();
        }

        protected override void WriteGetMemberBinder(GetMemberBinder getMemberBinder, IList<Expression> args) {
            throw new NotImplementedException();
        }

        protected override void WriteInvokeBinder(InvokeBinder invokeBinder, IList<Expression> args) {
            throw new NotImplementedException();
        }

        protected override void WriteInvokeMemberBinder(InvokeMemberBinder invokeMemberBinder, IList<Expression> args) {
            throw new NotImplementedException();
        }

        protected override void WriteSetIndexBinder(SetIndexBinder setIndexBinder, IList<Expression> args) {
            throw new NotImplementedException();
        }

        protected override void WriteSetMemberBinder(SetMemberBinder setMemberBinder, IList<Expression> args) {
            throw new NotImplementedException();
        }

        protected override void WriteUnaryOperationBinder(UnaryOperationBinder unaryOperationBinder, IList<Expression> args) {
            throw new NotImplementedException();
        }

        protected override void WriteParameterDeclaration(ParameterExpression prm) {
            throw new NotImplementedException();
        }

        private static readonly Dictionary<Type, string> typeAliases = new Dictionary<Type, string> {
            {typeof(int), "int"},
            {typeof(uint), "uint"},
            {typeof(short), "short"},
            {typeof(ushort), "ushort"},
            {typeof(long), "long"},
            {typeof(ulong), "ulong"},
            {typeof(bool), "bool"},
            {typeof(float), "float"},
        };

        private static string typeName(Type t) =>
            t.IsNullable() ?
                typeName(t.UnderlyingIfNullable()) + "?" :
                typeAliases.TryGetValue(t, out var name) ?
                    name :
                    t.Name;
    }
}
