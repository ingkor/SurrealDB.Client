namespace SurrealDB.Client.Query;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

/// <summary>
/// Compiles LINQ expression trees to SurrealQL query strings.
/// Translates method calls, comparisons, and projections to SurrealDB SQL syntax.
/// </summary>
public class SurrealQueryCompiler : IQueryCompiler
{
    /// <summary>
    /// Compiles an expression tree to SurrealQL.
    /// </summary>
    public string Compile(Expression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var visitor = new SurrealQLExpressionVisitor(null);
        visitor.Visit(expression);
        return visitor.GetSQL();
    }

    /// <summary>
    /// Compiles an expression tree with detailed metadata.
    /// </summary>
    public CompiledQuery CompileDetailed(Expression expression, string? tableName = null)
    {
        ArgumentNullException.ThrowIfNull(expression);

        var visitor = new SurrealQLExpressionVisitor(tableName);
        visitor.Visit(expression);
        var sql = visitor.GetSQL();

        return new CompiledQuery
        {
            SurrealQL = sql,
            TableName = tableName ?? visitor.GetTableName(),
            Parameters = visitor.GetParameters(),
            IsScalar = visitor.IsScalarQuery,
            EntityType = visitor.GetEntityType()
        };
    }
}

/// <summary>
/// Expression tree visitor that converts LINQ to SurrealQL.
/// Handles Where, OrderBy, OrderByDescending, Select, Take, Skip, and comparisons.
/// </summary>
internal class SurrealQLExpressionVisitor : ExpressionVisitor
{
    private StringBuilder _sql = new();
    private readonly Dictionary<string, object?> _parameters = new();
    private string? _tableName;
    private Type? _entityType;
    private string? _selectClause = "SELECT *";
    private string? _whereClause;
    private string? _orderByClause;
    private int? _takeCount;
    private int? _skipCount;
    private int _parameterIndex;
    private bool _inWhere;
    private bool _isScalar;

    public SurrealQLExpressionVisitor(string? tableName)
    {
        _tableName = tableName;
    }

    public bool IsScalarQuery => _isScalar;

    /// <summary>
    /// Gets the generated SQL.
    /// </summary>
    public string GetSQL()
    {
        var sql = new StringBuilder();

        // SELECT clause
        if (!string.IsNullOrEmpty(_selectClause))
            sql.Append(_selectClause);

        // FROM clause
        if (!string.IsNullOrEmpty(_tableName))
            sql.Append($" FROM {_tableName}");

        // WHERE clause
        if (!string.IsNullOrEmpty(_whereClause))
            sql.Append($" WHERE {_whereClause}");

        // ORDER BY clause
        if (!string.IsNullOrEmpty(_orderByClause))
            sql.Append($" ORDER BY {_orderByClause}");

        // LIMIT and OFFSET
        if (_skipCount.HasValue)
            sql.Append($" LIMIT {_takeCount ?? 1000000} START {_skipCount}");
        else if (_takeCount.HasValue)
            sql.Append($" LIMIT {_takeCount}");

        return sql.ToString();
    }

    /// <summary>
    /// Gets the table name extracted from the query.
    /// </summary>
    public string? GetTableName() => _tableName;

    /// <summary>
    /// Gets the entity type from the query.
    /// </summary>
    public Type? GetEntityType() => _entityType;

    /// <summary>
    /// Gets compiled parameters.
    /// </summary>
    public Dictionary<string, object?> GetParameters() => _parameters;

    /// <summary>
    /// Visits MethodCallExpression to handle LINQ methods.
    /// </summary>
    public override Expression? VisitMethodCall(MethodCallExpression node)
    {
        // Handle query methods
        var method = node.Method;

        if (method.Name == "Where" && node.Arguments.Count == 2)
        {
            // First argument is the source (previous query)
            Visit(node.Arguments[0]);

            // Second argument is the predicate lambda
            if (node.Arguments[1] is LambdaExpression lambda)
            {
                _inWhere = true;
                _whereClause = VisitLambdaToSQL(lambda);
                _inWhere = false;
            }
        }
        else if (method.Name == "OrderBy" && node.Arguments.Count == 2)
        {
            Visit(node.Arguments[0]);

            if (node.Arguments[1] is LambdaExpression lambda)
            {
                _orderByClause = VisitOrderByLambda(lambda, ascending: true);
            }
        }
        else if (method.Name == "OrderByDescending" && node.Arguments.Count == 2)
        {
            Visit(node.Arguments[0]);

            if (node.Arguments[1] is LambdaExpression lambda)
            {
                _orderByClause = VisitOrderByLambda(lambda, ascending: false);
            }
        }
        else if (method.Name == "Select" && node.Arguments.Count == 2)
        {
            Visit(node.Arguments[0]);

            if (node.Arguments[1] is LambdaExpression lambda)
            {
                _selectClause = VisitSelectLambda(lambda);
            }
        }
        else if (method.Name == "Take" && node.Arguments.Count == 2)
        {
            Visit(node.Arguments[0]);

            if (node.Arguments[1] is ConstantExpression constExpr && constExpr.Value is int take)
            {
                _takeCount = take;
            }
        }
        else if (method.Name == "Skip" && node.Arguments.Count == 2)
        {
            Visit(node.Arguments[0]);

            if (node.Arguments[1] is ConstantExpression constExpr && constExpr.Value is int skip)
            {
                _skipCount = skip;
            }
        }
        else if (method.Name == "Count" && node.Arguments.Count == 1)
        {
            _selectClause = "SELECT count()";
            _isScalar = true;
            Visit(node.Arguments[0]);
        }
        else if (method.Name == "First" || method.Name == "FirstOrDefault")
        {
            _takeCount = 1;
            _isScalar = true;
            Visit(node.Arguments[0]);
        }
        else if (method.Name == "Any")
        {
            _selectClause = "SELECT 1";
            _isScalar = true;
            Visit(node.Arguments[0]);
        }
        else
        {
            // Default: just visit the source
            Visit(node.Arguments[0]);
        }

        return node;
    }

    /// <summary>
    /// Visits a lambda expression for WHERE clause.
    /// </summary>
    private string VisitLambdaToSQL(LambdaExpression lambda)
    {
        if (lambda.Body is BinaryExpression binaryExpr)
        {
            return VisitBinaryToSQL(binaryExpr, lambda.Parameters[0]);
        }

        return string.Empty;
    }

    /// <summary>
    /// Visits binary expression (comparisons, logical operators).
    /// </summary>
    private string VisitBinaryToSQL(BinaryExpression expr, ParameterExpression parameter)
    {
        var left = VisitExpressionToSQL(expr.Left, parameter);
        var right = VisitExpressionToSQL(expr.Right, parameter);

        var op = expr.NodeType switch
        {
            ExpressionType.Equal => "=",
            ExpressionType.NotEqual => "!=",
            ExpressionType.GreaterThan => ">",
            ExpressionType.GreaterThanOrEqual => ">=",
            ExpressionType.LessThan => "<",
            ExpressionType.LessThanOrEqual => "<=",
            ExpressionType.AndAlso => "AND",
            ExpressionType.OrElse => "OR",
            _ => "="
        };

        return $"{left} {op} {right}";
    }

    /// <summary>
    /// Converts an expression to SQL string.
    /// </summary>
    private string VisitExpressionToSQL(Expression expr, ParameterExpression parameter)
    {
        if (expr is MemberExpression memberExpr)
        {
            // Property access (u.Age, u.Name)
            if (memberExpr.Expression is ParameterExpression param && param.Name == parameter.Name)
            {
                return ToSnakeCase(memberExpr.Member.Name);
            }
        }

        if (expr is ConstantExpression constExpr)
        {
            // Constant value
            var paramName = $"@p{_parameterIndex++}";
            _parameters[paramName] = constExpr.Value;
            return paramName;
        }

        if (expr is BinaryExpression binaryExpr)
        {
            // Nested binary expression
            return VisitBinaryToSQL(binaryExpr, parameter);
        }

        if (expr is UnaryExpression unaryExpr && unaryExpr.NodeType == ExpressionType.Not)
        {
            var inner = VisitExpressionToSQL(unaryExpr.Operand, parameter);
            return $"NOT ({inner})";
        }

        return string.Empty;
    }

    /// <summary>
    /// Visits a lambda for ORDER BY clause.
    /// </summary>
    private string VisitOrderByLambda(LambdaExpression lambda, bool ascending)
    {
        var body = lambda.Body;
        var propertyName = string.Empty;

        if (body is MemberExpression memberExpr)
        {
            propertyName = ToSnakeCase(memberExpr.Member.Name);
        }

        if (!ascending)
            propertyName += " DESC";

        return propertyName;
    }

    /// <summary>
    /// Visits a lambda for SELECT clause (projection).
    /// </summary>
    private string VisitSelectLambda(LambdaExpression lambda)
    {
        // For now, support simple projections
        // Full support would map all properties in the expression
        return "SELECT *";
    }

    /// <summary>
    /// Visits a Constant expression and extracts table name.
    /// </summary>
    public override Expression? VisitConstant(ConstantExpression node)
    {
        if (node.Value is SurrealDbQuery<> query)
        {
            // Extract table name from SurrealDbQuery metadata if available
            _tableName = "unknown_table";
        }

        return base.VisitConstant(node);
    }

    /// <summary>
    /// Converts property names to snake_case for SurrealDB.
    /// </summary>
    private static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder();
        var chars = name.ToCharArray();

        for (var i = 0; i < chars.Length; i++)
        {
            if (char.IsUpper(chars[i]) && i > 0)
            {
                sb.Append('_');
                sb.Append(char.ToLower(chars[i]));
            }
            else
            {
                sb.Append(char.ToLower(chars[i]));
            }
        }

        return sb.ToString();
    }
}
