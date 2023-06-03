using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

namespace Lambda.Dynamic.SelectExpression
{
    public static class LabmdaDynamicSelectExpressions
    {
        private static readonly Type[] primitiveTypes = new[] {
            typeof(Boolean),
            typeof(Byte),
            typeof(SByte),
            typeof(Int16),
            typeof(UInt16),
            typeof(Int32),
            typeof(UInt32),
            typeof(Int64),
            typeof(UInt64),
            typeof(IntPtr),
            typeof(UIntPtr),
            typeof(Char),
            typeof(Double),
            typeof(Single),

            //EXTRAS
            typeof(Decimal),
            typeof(DateTime),
            typeof(Guid),
            typeof(String),

            //NULLABLES
            typeof(Boolean?),
            typeof(Byte?),
            typeof(SByte?),
            typeof(Int16?),
            typeof(UInt16?),
            typeof(Int32?),
            typeof(UInt32?),
            typeof(Int64?),
            typeof(UInt64?),
            typeof(IntPtr?),
            typeof(UIntPtr?),
            typeof(Char?),
            typeof(Double?),
            typeof(Single?),
            typeof(Decimal?),
            typeof(DateTime?),
            typeof(Guid?)
        };

        public record MappingProperty(PropertyInfo TResponseProperty, PropertyInfo TEntityProperty);

        private static IEnumerable<MappingProperty> GetSharedProperties(Type TEntityType, Type TResponseType)
        {
            return TResponseType.GetProperties()
                .Where(e => TEntityType.GetProperties().Any(a => a.Name == e.Name))
                //.Where(e => primitiveTypes.Contains(e.PropertyType) /*|| typeof(IEnumerable).IsAssignableFrom(e.PropertyType) == false*/)
                .Select(e =>
                {
                    return new MappingProperty(TResponseType.GetProperty(e.Name), TEntityType.GetProperty(e.Name));
                });
        }

        private static Expression GenericSelectExpression(Type tEntityType, Type tResponseType)
        {
            var sharedProperties = GetSharedProperties(tEntityType, tResponseType);
            var sharedParameter = Expression.Parameter(tEntityType, tEntityType.Name);
            var sharedBindings = new List<MemberAssignment>();

            foreach (var sharedProperty in sharedProperties)
            {
                if (primitiveTypes.Contains(sharedProperty.TResponseProperty.PropertyType))
                {
                    sharedBindings.Add(Expression.Bind(sharedProperty.TResponseProperty, Expression.Property(sharedParameter, sharedProperty.TEntityProperty)));
                    continue;
                }

                if (typeof(IEnumerable).IsAssignableFrom(sharedProperty.TEntityProperty.PropertyType))
                {
                    continue;
                }

                if (sharedProperty.TEntityProperty.IsNullableReferenceType())
                {
                    //BindNullableNestedObject(sharedProperty, sharedParameter, null, sharedBindings);
                    continue;
                }

                BindNestedObject(sharedProperty, sharedParameter, null, sharedBindings);
            };

            var tResponseObject = Expression.New(tResponseType);
            var tResponseObjectInit = Expression.MemberInit(tResponseObject, sharedBindings);
            return Expression.Lambda(tResponseObjectInit, sharedParameter);
        }

        public static Expression<Func<TEntity, TResponse>> GenericSelectExpression<TEntity, TResponse>()
        {
            var tEntityType = typeof(TEntity);
            var tResponseType = typeof(TResponse);


            var sharedProperties = GetSharedProperties(tEntityType, tResponseType);
            var sharedParameter = Expression.Parameter(tEntityType, tEntityType.Name);
            var sharedBindings = new List<MemberAssignment>();

            foreach (var sharedProperty in sharedProperties)
            {
                if (primitiveTypes.Contains(sharedProperty.TResponseProperty.PropertyType))
                {
                    sharedBindings.Add(Expression.Bind(sharedProperty.TResponseProperty, Expression.Property(sharedParameter, sharedProperty.TEntityProperty)));
                    continue;
                }

                if (typeof(IEnumerable).IsAssignableFrom(sharedProperty.TEntityProperty.PropertyType))
                {
                    var innerEntityType = sharedProperty.TEntityProperty.PropertyType.GetGenericArguments()[0];
                    var innerResponseType = sharedProperty.TResponseProperty.PropertyType.GetGenericArguments()[0];
                    var enumerableType = typeof(IEnumerable<>).MakeGenericType(innerResponseType);
                    var innerParameter = Expression.Parameter(innerEntityType, "inner");
                    var innerLambda = GenericSelectExpression(innerEntityType, innerResponseType);
                    var selectMethod = typeof(Enumerable).GetMethods()
                        .First(m => m.Name == "Select" && m.GetParameters().Length == 2)
                        .MakeGenericMethod(innerEntityType, innerResponseType);

                    var propertyAccess = Expression.Property(sharedParameter, sharedProperty.TEntityProperty);
                    var selectCall = Expression.Call(null, selectMethod, propertyAccess, innerLambda);

                    sharedBindings.Add(Expression.Bind(sharedProperty.TResponseProperty, Expression.Convert(selectCall, enumerableType)));
                    continue;
                }

                if (sharedProperty.TEntityProperty.IsNullableReferenceType())
                {
                    BindNullableNestedObject(sharedProperty, sharedParameter, null, sharedBindings);
                    continue;
                }
                BindNestedObject(sharedProperty, sharedParameter, null, sharedBindings);
            };

            var tResponseObject = Expression.New(typeof(TResponse));
            var tResponseObjectInit = Expression.MemberInit(tResponseObject, sharedBindings);
            return Expression.Lambda<Func<TEntity, TResponse>>(tResponseObjectInit, sharedParameter);
        }

        private static MemberInitExpression CreateNestedMemberInitExpression(MappingProperty sharedProperty, ParameterExpression sharedParameter, IEnumerable<MemberAssignment> tEntitySharedNestedObjectBindings)
        {
            NewExpression tResponseNestedObject = Expression.New(sharedProperty.TResponseProperty.PropertyType);
            MemberInitExpression tResponseNestedObjectInit = Expression.MemberInit(tResponseNestedObject, tEntitySharedNestedObjectBindings);
            return tResponseNestedObjectInit;
        }

        private static IEnumerable<MemberAssignment> GetNestedObjectBindings(MappingProperty sharedProperty, ParameterExpression sharedParameter, List<MemberAssignment> parentBindings)
        {
            MemberExpression tEntityNestedObjectExpression = Expression.Property(sharedParameter, sharedProperty.TEntityProperty);
            var sharedNestedObjectProperties = GetSharedProperties(sharedProperty.TEntityProperty.PropertyType, sharedProperty.TResponseProperty.PropertyType);
            var tEntitySharedNestedObjectBindings = new List<MemberAssignment>();

            foreach (var tEntitySharedNestedObjectProperty in sharedNestedObjectProperties)
            {
                if (primitiveTypes.Contains(tEntitySharedNestedObjectProperty.TResponseProperty.PropertyType))
                {
                    MemberExpression tEntityNestedObjectProperty = Expression.Property(tEntityNestedObjectExpression, tEntitySharedNestedObjectProperty.TEntityProperty);
                    tEntitySharedNestedObjectBindings.Add(Expression.Bind(tEntitySharedNestedObjectProperty.TResponseProperty, tEntityNestedObjectProperty));
                    continue;
                }

                if (typeof(IEnumerable).IsAssignableFrom(sharedProperty.TEntityProperty.PropertyType))
                {
                    //throw new Exception($"GenericSelectExpression<{tEntityType.Name},{tResponseType.Name}> not Valid!");
                }

                //var nestedParameter = Expression.Parameter(sharedProperty.TEntityProperty.PropertyType, $"{sharedParameter.Name}.{sharedProperty.TEntityProperty.Name}");
                //var nestedObjectBinding = BindNestedObject(tEntitySharedNestedObjectProperty, sharedParameter, parentBindings, tEntitySharedNestedObjectBindings, nestedParameter);
                continue;
            };
            return tEntitySharedNestedObjectBindings;
        }

        public static bool IsNullableReferenceType(this PropertyInfo property)
        {
            // Get the custom attributes of the property
            var customAttributes = property.GetCustomAttributesData();

            // Find the NullableAttribute
            return customAttributes.Any(a => a.AttributeType.Name == "NullableAttribute");
        }

        private static MemberAssignment BindNullableNestedObject(MappingProperty nestedObjectProperty, ParameterExpression sharedParameter, List<MemberAssignment> parentObjectBindings, List<MemberAssignment>? nestedObjectBindings = null, ParameterExpression? nestedObjectParameter = null)
        {
            IEnumerable<MemberAssignment> nestedObjectBindings2 = GetNestedObjectBindings(nestedObjectProperty, nestedObjectParameter ?? sharedParameter, nestedObjectBindings ?? parentObjectBindings);

            // Create the nested property access expression
            Expression nestedPropertyAccess = Expression.Property(nestedObjectParameter ?? sharedParameter, nestedObjectProperty.TEntityProperty);

            // Create a condition that checks if the nested property is null
            Expression nullCheck = Expression.NotEqual(nestedPropertyAccess, Expression.Constant(null, nestedObjectProperty.TEntityProperty.PropertyType));

            // Create the MemberInit expression for the nested object
            MemberInitExpression memberInitExpression = CreateNestedMemberInitExpression(nestedObjectProperty, nestedObjectParameter ?? sharedParameter, nestedObjectBindings2);

            // Create a condition expression that returns the MemberInit expression if the nested object is not null, otherwise null
            Expression conditionExpression = Expression.Condition(nullCheck, memberInitExpression, Expression.Constant(null, nestedObjectProperty.TResponseProperty.PropertyType));

            // Create the MemberAssignment for the nested object property
            MemberAssignment memberAssignment = Expression.Bind(nestedObjectProperty.TResponseProperty, conditionExpression);

            // Add the MemberAssignment to the bindings
            (nestedObjectBindings ?? parentObjectBindings)!.Add(memberAssignment);

            return memberAssignment;
        }

        private static MemberAssignment BindNestedObject(MappingProperty nestedObjectProperty, ParameterExpression sharedParameter, List<MemberAssignment> parentObjectBindings, List<MemberAssignment>? nestedObjectBindings = null, ParameterExpression? nestedObjectParameter = null)
        {
            var nestedObjectBingings = GetNestedObjectBindings(nestedObjectProperty, nestedObjectParameter ?? sharedParameter, nestedObjectBindings ?? parentObjectBindings);
            MemberInitExpression tResponseNestedObjectInit = CreateNestedMemberInitExpression(nestedObjectProperty, nestedObjectParameter ?? sharedParameter, nestedObjectBingings);
            (nestedObjectBindings ?? parentObjectBindings).Add(Expression.Bind(nestedObjectProperty.TResponseProperty, tResponseNestedObjectInit));
            return Expression.Bind(nestedObjectProperty.TResponseProperty, tResponseNestedObjectInit);
        }

        private static LambdaExpression CreateNestedLambdaExpression(MemberInitExpression tResponseNestedObjectInit, ParameterExpression correspondantParameter)
        {
            return Expression.Lambda(tResponseNestedObjectInit, correspondantParameter);
        }
    }

    #region Project().To<TType>()

    public static class QueryableExtensions
    {
        public static IProjectionExpression Project<TSource>(
            this IQueryable<TSource> source)
        {
            return new ProjectionExpression<TSource>(source);
        }
    }
    public interface IProjectionExpression
    {
        IQueryable<TResult> To<TResult>();
    }
    public class ProjectionExpression<TSource> : IProjectionExpression
    {
        private readonly IQueryable<TSource> _source;

        public ProjectionExpression(IQueryable<TSource> source)
        {
            _source = source;
        }

        public IQueryable<TResult> To<TResult>()
        {
            Expression<Func<TSource, TResult>> expr = BuildExpression<TResult>();

            return _source.Select(expr);
        }

        public static Expression<Func<TSource, TResult>> BuildExpression<TResult>()
        {
            var sourceMembers = typeof(TSource).GetProperties();
            var destinationMembers = typeof(TResult).GetProperties();

            var name = "src";

            var parameterExpression = Expression.Parameter(typeof(TSource), name);
            var expressions = destinationMembers.ToArray();
            var bindings = expressions.Select(dest =>
            {
                var sourcemem = sourceMembers.First(pi => pi.Name == dest.Name);
                var prop = Expression.Property(parameterExpression, sourcemem);
                return Expression.Bind(dest, prop);
            });
            var mapping = Expression.MemberInit(Expression.New(typeof(TResult)), bindings);
            return Expression.Lambda<Func<TSource, TResult>>(mapping, parameterExpression);
        }
    }

    #endregion
}