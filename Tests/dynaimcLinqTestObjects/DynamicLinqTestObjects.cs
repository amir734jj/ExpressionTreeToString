﻿using ExpressionTreeTestObjects;
using System;
using System.Linq.Expressions;

namespace ExpressionTreeToString.Tests {
    [ObjectContainer]
    public static partial class DynamicLinqTestObjects {
        internal static Expression Expr<T>(Expression<Func<Person, T>> expr) => expr;
        internal static Expression Expr(Expression<Action<Person>> expr) => expr;

        [TestObject("Dynamic LINQ")]
        internal static readonly Expression Parameter = Expr(p => p);

        [TestObject("Dynamic LINQ")]
        internal static readonly Expression In = Expr(p => p.LastName == "A" || p.LastName == "B");

        [TestObject("Dynamic LINQ")]
        internal static readonly Expression MultipleFieldsIn = Expr(p => p.LastName == "A" || p.LastName == "B" || p.FirstName == "C" || p.FirstName == "D");

        // tests for multiple fields, some of which have a single value, and some of which have multiple vales
        [TestObject("Dynamic LINQ")]
        internal static readonly Expression MultipleFieldSingleValueIn = Expr(p => p.LastName == "A" || p.LastName == "B" || p.DOB == DateTime.MinValue || p.FirstName == "C" || p.FirstName == "D");

        // pending https://github.com/zzzprojects/System.Linq.Dynamic.Core/issues/441
        //[TestObject("Invocation")]
        //internal static readonly Expression Invocation = Expr(p => ((Func<string>)(() => p.LastName + p.FirstName))());

        // pending https://github.com/zzzprojects/System.Linq.Dynamic.Core/issues/441
        //[TestObject("Invocation")]
        //internal static readonly Expression InvocationArguments = Expr(p => ((Func<Person, string>)(p1 => p1.LastName + p1.FirstName))(p));

        [TestObject("Dynamic LINQ")]
        internal static readonly Expression ParameterTypeAs = Expr(p => p as object);

        // throws a NotImplemented exception; we currently have no way to check for this
        //[TestObject("Dynamic LINQ")]
        //internal static readonly Expression PropertyTypeAs = Expr(p => p.LastName as object);

        [TestObject("Dynamic LINQ")]
        internal static readonly Expression ParameterTypeIs = Expr(p => p is object);

        // throws a NotImplemented exception; we currently have no way to check for this
        //[TestObject("Dynamic LINQ")]
        //internal static readonly Expression PropertyTypeIs = Expr(p => p.LastName is object);
    }
}
