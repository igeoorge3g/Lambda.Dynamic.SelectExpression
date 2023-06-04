using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

namespace Lambda.Dynamic.SelectExpression
{
    public static class LambdaDynamicSelectExpressions
    {
        private static readonly Dictionary<(Type, Type), IEnumerable<MappingProperty>> sharedPropertiesCache = new();
        private static readonly HashSet<Type> primitiveTypes = new HashSet<Type>
        {
            typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
            typeof(int), typeof(uint), typeof(long), typeof(ulong),
            typeof(IntPtr), typeof(UIntPtr), typeof(char), typeof(double), typeof(float),
            typeof(decimal), typeof(DateTime), typeof(Guid), typeof(string),
            typeof(bool?), typeof(byte?), typeof(sbyte?), typeof(short?), typeof(ushort?),
            typeof(int?), typeof(uint?), typeof(long?), typeof(ulong?),
            typeof(IntPtr?), typeof(UIntPtr?), typeof(char?), typeof(double?), typeof(float?),
            typeof(decimal?), typeof(DateTime?), typeof(Guid?)
        };

        public static Expression<Func<TEntity, TResponse>> GenericSelectExpression<TEntity, TResponse>()
        {
            var tEntityType = typeof(TEntity);
            var tResponseType = typeof(TResponse);


            var sharedProperties = GetSharedProperties(tEntityType, tResponseType);
            var sharedParameter = Expression.Parameter(tEntityType, tEntityType.Name);
            List<MemberAssignment> sharedBindings = new();

            foreach (var sharedProperty in sharedProperties)
            {
                if (primitiveTypes.Contains(sharedProperty.TResponseProperty.PropertyType))
                {
                    var propertyAccess = Expression.Property(sharedParameter, sharedProperty.TEntityProperty);
                    var memberAssignment = Expression.Bind(sharedProperty.TResponseProperty, propertyAccess);
                    sharedBindings.Add(memberAssignment);
                    continue;
                }

                if (sharedProperty.TEntityProperty.PropertyType.IsEnum)
                {
                    var propertyAccess = Expression.Property(sharedParameter, sharedProperty.TEntityProperty);
                    var convertExpression = Expression.Convert(propertyAccess, sharedProperty.TResponseProperty.PropertyType);
                    var memberAssignment = Expression.Bind(sharedProperty.TResponseProperty, convertExpression);
                    sharedBindings.Add(memberAssignment);
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
                    var convertExpression = Expression.Convert(selectCall, enumerableType);
                    var memberAssignment = Expression.Bind(sharedProperty.TResponseProperty, convertExpression);
                    sharedBindings.Add(memberAssignment);
                    continue;
                }

                if (sharedProperty.TEntityProperty.IsNullableReferenceType())
                {
                    BindNullableNestedObject(sharedProperty, sharedParameter, sharedBindings);
                    continue;
                }
                BindNestedObject(sharedProperty, sharedParameter, sharedBindings);
            };

            var tResponseObject = Expression.New(typeof(TResponse));
            var tResponseObjectInit = Expression.MemberInit(tResponseObject, sharedBindings);
            return Expression.Lambda<Func<TEntity, TResponse>>(tResponseObjectInit, sharedParameter);
        }

        private static Expression GenericSelectExpression(Type tEntityType, Type tResponseType)
        {
            var sharedProperties = GetSharedProperties(tEntityType, tResponseType);
            var sharedParameter = Expression.Parameter(tEntityType, tEntityType.Name);
            List<MemberAssignment> sharedBindings = new();

            foreach (var sharedProperty in sharedProperties)
            {
                if (primitiveTypes.Contains(sharedProperty.TResponseProperty.PropertyType))
                {
                    var propertyAccess = Expression.Property(sharedParameter, sharedProperty.TEntityProperty);
                    var memberAssignment = Expression.Bind(sharedProperty.TResponseProperty, propertyAccess);
                    sharedBindings.Add(memberAssignment);
                    continue;
                }

                if (sharedProperty.TEntityProperty.PropertyType.IsEnum)
                {
                    var propertyAccess = Expression.Property(sharedParameter, sharedProperty.TEntityProperty);
                    var convertExpression = Expression.Convert(propertyAccess, sharedProperty.TResponseProperty.PropertyType);
                    var memberAssignment = Expression.Bind(sharedProperty.TResponseProperty, convertExpression);
                    sharedBindings.Add(memberAssignment);
                    continue;
                }

                if (typeof(IEnumerable).IsAssignableFrom(sharedProperty.TEntityProperty.PropertyType))
                {
                    continue;
                }

                if (sharedProperty.TEntityProperty.IsNullableReferenceType())
                {
                    BindNullableNestedObject(sharedProperty, sharedParameter, sharedBindings);
                    continue;
                }

                BindNestedObject(sharedProperty, sharedParameter, sharedBindings);
            };

            var tResponseObject = Expression.New(tResponseType);
            var tResponseObjectInit = Expression.MemberInit(tResponseObject, sharedBindings);
            return Expression.Lambda(tResponseObjectInit, sharedParameter);
        }

        private record MappingProperty(PropertyInfo TResponseProperty, PropertyInfo TEntityProperty);

        private static IEnumerable<MappingProperty> GetSharedProperties(Type TEntityType, Type TResponseType)
        {
            var key = (TEntityType, TResponseType);

            if (!sharedPropertiesCache.ContainsKey(key))
            {
                var properties = TResponseType.GetProperties()
                    .Where(e => TEntityType.GetProperties().Any(a => a.Name == e.Name))
                    .Select(e =>
                    {
                        return new MappingProperty(TResponseType.GetProperty(e.Name), TEntityType.GetProperty(e.Name));
                    });

                sharedPropertiesCache[key] = properties;
            }

            return sharedPropertiesCache[key];
        }

        private static MemberInitExpression CreateNestedMemberInitExpression(MappingProperty sharedProperty, IEnumerable<MemberAssignment> tEntitySharedNestedObjectBindings)
        {
            NewExpression tResponseNestedObject = Expression.New(sharedProperty.TResponseProperty.PropertyType);
            MemberInitExpression tResponseNestedObjectInit = Expression.MemberInit(tResponseNestedObject, tEntitySharedNestedObjectBindings);
            return tResponseNestedObjectInit;
        }

        private static IEnumerable<MemberAssignment> GetNestedObjectBindings(MappingProperty sharedProperty, ParameterExpression sharedParameter)
        {
            MemberExpression tEntityNestedObjectExpression = Expression.Property(sharedParameter, sharedProperty.TEntityProperty);
            var sharedNestedObjectProperties = GetSharedProperties(sharedProperty.TEntityProperty.PropertyType, sharedProperty.TResponseProperty.PropertyType);
            List<MemberAssignment> tEntitySharedNestedObjectBindings = new();

            foreach (var tEntitySharedNestedObjectProperty in sharedNestedObjectProperties)
            {
                if (primitiveTypes.Contains(tEntitySharedNestedObjectProperty.TResponseProperty.PropertyType))
                {
                    MemberExpression tEntityNestedObjectProperty = Expression.Property(tEntityNestedObjectExpression, tEntitySharedNestedObjectProperty.TEntityProperty);
                    tEntitySharedNestedObjectBindings.Add(Expression.Bind(tEntitySharedNestedObjectProperty.TResponseProperty, tEntityNestedObjectProperty));
                    continue;
                }

                if (sharedProperty.TEntityProperty.PropertyType.IsEnum)
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

        private static void BindNullableNestedObject(MappingProperty nestedObjectProperty, ParameterExpression sharedParameter, List<MemberAssignment> nestedObjectBindings)
        {
            IEnumerable<MemberAssignment> nestedObjectBindings2 = GetNestedObjectBindings(nestedObjectProperty, sharedParameter);

            // Create the nested property access expression
            Expression nestedPropertyAccess = Expression.Property(sharedParameter, nestedObjectProperty.TEntityProperty);

            // Create a condition that checks if the nested property is null
            Expression nullCheck = Expression.NotEqual(nestedPropertyAccess, Expression.Constant(null, nestedObjectProperty.TEntityProperty.PropertyType));

            // Create the MemberInit expression for the nested object
            MemberInitExpression memberInitExpression = CreateNestedMemberInitExpression(nestedObjectProperty, nestedObjectBindings2);

            // Create a condition expression that returns the MemberInit expression if the nested object is not null, otherwise null
            Expression conditionExpression = Expression.Condition(nullCheck, memberInitExpression, Expression.Constant(null, nestedObjectProperty.TResponseProperty.PropertyType));

            // Create the MemberAssignment for the nested object property
            MemberAssignment memberAssignment = Expression.Bind(nestedObjectProperty.TResponseProperty, conditionExpression);

            // Add the MemberAssignment to the bindings
            nestedObjectBindings.Add(memberAssignment);
        }

        private static void BindNestedObject(MappingProperty nestedObjectProperty, ParameterExpression sharedParameter, List<MemberAssignment> nestedObjectBindings)
        {
            var nestedObjectBingings = GetNestedObjectBindings(nestedObjectProperty, sharedParameter);
            MemberInitExpression tResponseNestedObjectInit = CreateNestedMemberInitExpression(nestedObjectProperty, nestedObjectBingings);
            var memberAssignment = Expression.Bind(nestedObjectProperty.TResponseProperty, tResponseNestedObjectInit);
            nestedObjectBindings.Add(memberAssignment);
        }

        private static bool IsNullableReferenceType(this PropertyInfo property) => property.GetCustomAttributesData().Any(a => a.AttributeType.Name == "NullableAttribute");
    }
}
