using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace AspNetCore.IQueryable.Extensions.Filter
{
    public static class FiltersExtensions
    {
        public static IQueryable<TEntity> Filter<TEntity>(this IQueryable<TEntity> result, ICustomQueryable model)
        {
            if (model == null)
            {
                return result;
            }

            var lastExpression = result.FilterExpression(model);
            return lastExpression == null
                ? result
                : result.Where(lastExpression);
        }

        public static Expression<Func<TEntity, bool>> FilterExpression<TEntity>(this IQueryable<TEntity> result, ICustomQueryable model)
        {
            if (model == null)
            {
                return null;
            }

            Expression lastExpression = null;

            var operations = ExpressionFactory.GetOperators<TEntity>(model);

            if (operations.Any(a => a.Criteria.IgnoreProperty))
            {
                return null;
            }

            foreach (var expression in operations.Ordered())
            {
                if (!expression.Criteria.CaseSensitive)
                {
                    expression.FieldToFilter = Expression.Call(expression.FieldToFilter,
                        typeof(string).GetMethods()
                            .First(m => m.Name == "ToUpper" && m.GetParameters().Length == 0));

                    expression.FilterBy = Expression.Call(expression.FilterBy,
                        typeof(string).GetMethods()
                            .First(m => m.Name == "ToUpper" && m.GetParameters().Length == 0));
                }

                var actualExpression = GetExpression<TEntity>(expression);

                if (expression.Criteria.UseNot)
                {
                    actualExpression = Expression.Not(actualExpression);
                }

                if (lastExpression == null)
                {
                    lastExpression = actualExpression;
                }
                else
                {
                    if (expression.Criteria.UseOr)
                        lastExpression = Expression.Or(lastExpression, actualExpression);
                    else
                        lastExpression = Expression.And(lastExpression, actualExpression);
                }
            }

            return lastExpression != null ? Expression.Lambda<Func<TEntity, bool>>(lastExpression, operations.ParameterExpression) : null;
        }

        private static Expression GetExpression<TEntity>(ExpressionParser expression)
        {
            if (expression.FieldToFilter.Type != expression.FilterBy.Type)
            {
                if (Nullable.GetUnderlyingType(expression.FieldToFilter.Type) != null)
                {
                    expression.FilterBy = Expression.Convert(expression.FilterBy, expression.FieldToFilter.Type);
                }
                else if (Nullable.GetUnderlyingType(expression.FilterBy.Type) != null)
                {
                    expression.FieldToFilter = Expression.Convert(expression.FieldToFilter, expression.FilterBy.Type);
                }
            }

            return expression.Criteria.Operator switch
            {
                WhereOperator.Equals => Expression.Equal(expression.FieldToFilter, expression.FilterBy),
                WhereOperator.NotEquals => Expression.NotEqual(expression.FieldToFilter, expression.FilterBy),
                WhereOperator.GreaterThan => Expression.GreaterThan(expression.FieldToFilter, expression.FilterBy),
                WhereOperator.LessThan => Expression.LessThan(expression.FieldToFilter, expression.FilterBy),
                WhereOperator.GreaterThanOrEqualTo => Expression.GreaterThanOrEqual(expression.FieldToFilter, expression.FilterBy),
                WhereOperator.LessThanOrEqualTo => Expression.LessThanOrEqual(expression.FieldToFilter, expression.FilterBy),
                WhereOperator.Contains => ContainsExpression<TEntity>(expression),
                WhereOperator.GreaterThanOrEqualWhenNullable => GreaterThanOrEqualWhenNullable(expression.FieldToFilter, expression.FilterBy),
                WhereOperator.LessThanOrEqualWhenNullable => LessThanOrEqualWhenNullable(expression.FieldToFilter, expression.FilterBy),
                WhereOperator.EqualsWhenNullable => EqualsWhenNullable(expression.FieldToFilter, expression.FilterBy),
                WhereOperator.StartsWith => Expression.Call(expression.FieldToFilter,
                    typeof(string).GetMethods().First(m => m.Name == "StartsWith" && m.GetParameters().Length == 1), expression.FilterBy),
                _ => Expression.Equal(expression.FieldToFilter, expression.FilterBy),
            };
        }

        private static Expression LessThanOrEqualWhenNullable(Expression e1, Expression e2)
        {
            if (IsNullableType(e1.Type) && !IsNullableType(e2.Type))
                e2 = Expression.Convert(e2, e1.Type);
            else if (!IsNullableType(e1.Type) && IsNullableType(e2.Type))
                e1 = Expression.Convert(e1, e2.Type);

            return Expression.LessThanOrEqual(e1, e2);
        }

        private static Expression GreaterThanOrEqualWhenNullable(Expression e1, Expression e2)
        {
            if (IsNullableType(e1.Type) && !IsNullableType(e2.Type))
                e2 = Expression.Convert(e2, e1.Type);
            else if (!IsNullableType(e1.Type) && IsNullableType(e2.Type))
                e1 = Expression.Convert(e1, e2.Type);

            return Expression.GreaterThanOrEqual(e1, e2);
        }

        private static Expression EqualsWhenNullable(Expression e1, Expression e2)
        {
            if (IsNullableType(e1.Type) && !IsNullableType(e2.Type))
                e2 = Expression.Convert(e2, e1.Type);
            else if (!IsNullableType(e1.Type) && IsNullableType(e2.Type))
                e1 = Expression.Convert(e1, e2.Type);

            return Expression.Equal(e1, e2);
        }

        private static bool IsNullableType(Type t)
        {
            return t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>);
        }

        private static Expression ContainsExpression<TEntity>(ExpressionParser expression)
        {
            if (expression.Criteria.Property.IsPropertyACollection())
            {
                var methodToApplyContains = typeof(Enumerable).GetMethods(BindingFlags.Static | BindingFlags.Public)
                    .Single(x => x.Name == "Contains" && x.GetParameters().Length == 2)
                    .MakeGenericMethod(expression.FieldToFilter.Type);
                return Expression.Call(methodToApplyContains, expression.FilterBy, expression.FieldToFilter);
            }
            else
            {
                var methodToApplyContains = expression.FieldToFilter.Type.GetMethods()
                    .First(m => m.Name == "Contains" && m.GetParameters().Length == 1);

                return Expression.Call(expression.FieldToFilter, methodToApplyContains, expression.FilterBy);
            }
        }
    }
}