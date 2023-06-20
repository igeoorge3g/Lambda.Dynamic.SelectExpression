using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

namespace Lambda.Dynamic.SelectExpression
{
    public static class ProjectionHelper
    {
        private static readonly Dictionary<(Type, Type), IEnumerable<MappingProperty>> sharedPropertiesCache = new();
        private static readonly HashSet<Type> primitiveTypes = new HashSet<Type>(
            new[]
            {
                typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
                typeof(int), typeof(uint), typeof(long), typeof(ulong),
                typeof(IntPtr), typeof(UIntPtr), typeof(char), typeof(double), typeof(float),
                typeof(decimal), typeof(DateTime), typeof(Guid), typeof(string),
                typeof(bool?), typeof(byte?), typeof(sbyte?), typeof(short?), typeof(ushort?),
                typeof(int?), typeof(uint?), typeof(long?), typeof(ulong?),
                typeof(IntPtr?), typeof(UIntPtr?), typeof(char?), typeof(double?), typeof(float?),
                typeof(decimal?), typeof(DateTime?), typeof(Guid?)
            });

        private static IEnumerable<MappingProperty> GetSharedProperties(Type TEntityType, Type TResponseType)
        {
            var key = (TEntityType, TResponseType);

            if (!sharedPropertiesCache.TryGetValue(key, out var properties))
            {
                properties = TResponseType.GetProperties()
                    .Where(e => TEntityType.GetProperties().Any(a => a.Name == e.Name))
                    .Select(e =>
                    {
                        return new MappingProperty(TResponseType.GetProperty(e.Name)!, TEntityType.GetProperty(e.Name)!);
                    });

                sharedPropertiesCache[key] = properties;
            }

            return properties;
        }

        public static Expression<Func<TSource, TResult>> CreateProjection<TSource, TResult>()
        {
            var sourceType = typeof(TSource);
            var resultType = typeof(TResult);
            var sourceParameter = Expression.Parameter(sourceType, "source");

            var resultProperties = GetSharedProperties(sourceType, resultType);
            var bindings = CreateMemberBindings(sourceParameter, resultProperties);

            var memberInit = Expression.MemberInit(Expression.New(resultType), bindings);
            return Expression.Lambda<Func<TSource, TResult>>(memberInit, sourceParameter);
        }

        private static Expression CreateProjection(Type TSource, Type TResult)
        {
            var sourceParameter = Expression.Parameter(TSource, "source");

            var resultProperties = GetSharedProperties(TSource, TResult);
            var bindings = CreateMemberBindings(sourceParameter, resultProperties);

            var memberInit = Expression.MemberInit(Expression.New(TResult), bindings);
            return Expression.Lambda(memberInit, sourceParameter);
        }

        private static MemberBinding[] CreateMemberBindings(Expression sourceParameter, IEnumerable<MappingProperty> properties, int currentLevel = 1)
        {
            var bindings = new List<MemberBinding>();

            foreach (var sharedProperty in properties)
            {
                var sourceProperty = Expression.Property(sourceParameter, sharedProperty.TEntityProperty.Name);

                if (primitiveTypes.Contains(sharedProperty.TResponseProperty.PropertyType))
                {
                    var memberAssignment = Expression.Bind(sharedProperty.TResponseProperty, sourceProperty);
                    bindings.Add(memberAssignment);
                    continue;
                }

                if (sharedProperty.TResponseProperty.PropertyType.IsEnum)
                {
                    var propertyAccess = Expression.Property(sourceParameter, sharedProperty.TEntityProperty);
                    var convertExpression = Expression.Convert(propertyAccess, sharedProperty.TResponseProperty.PropertyType);
                    var memberAssignment = Expression.Bind(sharedProperty.TResponseProperty, convertExpression);
                    bindings.Add(memberAssignment);
                    continue;
                }


                if (typeof(IEnumerable).IsAssignableFrom(sharedProperty.TEntityProperty.PropertyType) && currentLevel == 1)
                {
                    var innerEntityType = sharedProperty.TEntityProperty.PropertyType.GetGenericArguments()[0];
                    var innerResponseType = sharedProperty.TResponseProperty.PropertyType.GetGenericArguments()[0];
                    var enumerableType = typeof(IEnumerable<>).MakeGenericType(innerResponseType);
                    var innerParameter = Expression.Parameter(innerEntityType, "inner");
                    var innerLambda = CreateProjection(innerEntityType, innerResponseType);
                    var selectMethod = typeof(Enumerable).GetMethods()
                        .First(m => m.Name == "Select" && m.GetParameters().Length == 2)
                        .MakeGenericMethod(innerEntityType, innerResponseType);

                    var propertyAccess = Expression.Property(sourceParameter, sharedProperty.TEntityProperty);
                    var selectCall = Expression.Call(null, selectMethod, propertyAccess, innerLambda);
                    var convertExpression = Expression.Convert(selectCall, enumerableType);
                    var memberAssignment = Expression.Bind(sharedProperty.TResponseProperty, convertExpression);
                    bindings.Add(memberAssignment);
                    continue;
                }

                if (currentLevel > 2)
                {
                    continue;
                }

                var childProperties = GetSharedProperties(sharedProperty.TEntityProperty.PropertyType, sharedProperty.TResponseProperty.PropertyType);
                var childBindings = CreateMemberBindings(sourceProperty, childProperties, currentLevel + 1);
                if (childBindings.Any() == false)
                {
                    continue;
                }


                if (sharedProperty.TEntityProperty.IsNullableReferenceType())
                {
                    var nullCheck = Expression.Equal(sourceProperty, Expression.Constant(null));
                    var memberInits = Expression.Condition(nullCheck, Expression.Default(sharedProperty.TResponseProperty.PropertyType), Expression.MemberInit(Expression.New(sharedProperty.TResponseProperty.PropertyType), childBindings));
                    var nullBinding = Expression.Bind(sharedProperty.TResponseProperty, memberInits);
                    bindings.Add(nullBinding);
                    continue;
                }

                var memberInit = Expression.MemberInit(Expression.New(sharedProperty.TResponseProperty.PropertyType), childBindings);
                var binding = Expression.Bind(sharedProperty.TResponseProperty, memberInit);
                bindings.Add(binding);
            }

            return bindings.ToArray();
        }

        private static bool IsNullableReferenceType(this PropertyInfo property) => property.PropertyType.GetCustomAttributesData().Any(a => a.AttributeType.Name == "NullableAttribute");
    }
}
