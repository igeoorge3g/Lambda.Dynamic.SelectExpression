using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Lambda.Dynamic.SelectExpression
{
    public static class LambdaDynamicSelectExpressions
    {
        private static readonly HashSet<Type> primitiveTypes = new HashSet<Type>
        {
            typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
            typeof(int), typeof(uint), typeof(long), typeof(ulong),
            typeof(IntPtr), typeof(UIntPtr), typeof(char), typeof(double), typeof(float),
            typeof(decimal), typeof(DateTime), typeof(Guid), typeof(string),
            typeof(bool?), typeof(byte?), typeof(sbyte?), typeof(short?), typeof(ushort?),
            typeof(int?), typeof(uint?), typeof(long?), typeof(ulong?),
            typeof(IntPtr?), typeof(UIntPtr?), typeof(char?), typeof(double?), typeof(float?),
            typeof(decimal?), typeof(DateTime?), typeof(Guid?),typeof(Enum)
        };

        public record MappingProperty(PropertyInfo TResponseProperty, PropertyInfo TEntityProperty);

        private static IEnumerable<MappingProperty> GetSharedProperties(Type entityType, Type responseType)
        {
            //return responseType.GetProperties()
            //    .Where(responseProperty => entityType.GetProperty(responseProperty.Name) != null)
            //    .Select(responseProperty => new MappingProperty(responseProperty, entityType.GetProperty(responseProperty.Name)!));
            return responseType.GetProperties()
                .Where(e => entityType.GetProperties().Any(a => a.Name == e.Name))
                //.Where(e => primitiveTypes.Contains(e.PropertyType) /*|| typeof(IEnumerable).IsAssignableFrom(e.PropertyType) == false*/)
                .Select(e =>
                {
                    return new MappingProperty(responseType.GetProperty(e.Name), entityType.GetProperty(e.Name));
                });
        }

        private static Expression GenericSelectExpression(Type entityType, Type responseType)
        {
            var sharedProperties = GetSharedProperties(entityType, responseType);
            var entityParameter = Expression.Parameter(entityType, entityType.Name);
            var sharedBindings = new List<MemberBinding>();

            foreach (var sharedProperty in sharedProperties)
            {
                if (primitiveTypes.Contains(sharedProperty.TResponseProperty.PropertyType))
                {
                    var entityProperty = Expression.Property(entityParameter, sharedProperty.TEntityProperty);
                    var binding = Expression.Bind(sharedProperty.TResponseProperty, entityProperty);
                    sharedBindings.Add(binding);
                    continue;
                }

                if (sharedProperty.TEntityProperty.PropertyType.IsEnum)
                {
                    var entityProperty = Expression.Property(entityParameter, sharedProperty.TEntityProperty);
                    var convert = Expression.Convert(entityProperty, sharedProperty.TResponseProperty.PropertyType);
                    var binding = Expression.Bind(sharedProperty.TResponseProperty, convert);
                    sharedBindings.Add(binding);
                    continue;
                }

                if (typeof(IEnumerable).IsAssignableFrom(sharedProperty.TEntityProperty.PropertyType))
                {
                    continue;
                }

                if (sharedProperty.TEntityProperty.IsNullableReferenceType())
                {
                    continue;
                }

                var nestedBindings = GetNestedObjectBindings(sharedProperty, entityParameter, sharedBindings);
                var memberInit = CreateMemberInitExpression(sharedProperty.TResponseProperty.PropertyType, nestedBindings);
                sharedBindings.Add(Expression.Bind(sharedProperty.TResponseProperty, memberInit));
            }

            var responseObject = Expression.New(responseType);
            var responseObjectInit = Expression.MemberInit(responseObject, sharedBindings);
            return Expression.Lambda(responseObjectInit, entityParameter);
        }

        public static Expression<Func<TEntity, TResponse>> GenericSelectExpression<TEntity, TResponse>()
        {
            var entityType = typeof(TEntity);
            var responseType = typeof(TResponse);

            var sharedProperties = GetSharedProperties(entityType, responseType);
            var entityParameter = Expression.Parameter(entityType, entityType.Name);
            var sharedBindings = new List<MemberBinding>();

            foreach (var sharedProperty in sharedProperties)
            {
                if (primitiveTypes.Contains(sharedProperty.TResponseProperty.PropertyType))
                {
                    var entityProperty = Expression.Property(entityParameter, sharedProperty.TEntityProperty);
                    var binding = Expression.Bind(sharedProperty.TResponseProperty, entityProperty);
                    sharedBindings.Add(binding);
                    continue;
                }

                if (sharedProperty.TEntityProperty.PropertyType.IsEnum)
                {
                    var entityProperty = Expression.Property(entityParameter, sharedProperty.TEntityProperty);
                    var convert = Expression.Convert(entityProperty, sharedProperty.TResponseProperty.PropertyType);
                    var binding = Expression.Bind(sharedProperty.TResponseProperty, convert);
                    sharedBindings.Add(binding);
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

                    var propertyAccess = Expression.Property(entityParameter, sharedProperty.TEntityProperty);
                    var selectCall = Expression.Call(selectMethod, propertyAccess, innerLambda);

                    sharedBindings.Add(Expression.Bind(sharedProperty.TResponseProperty, Expression.Convert(selectCall, enumerableType)));
                    continue;
                }

                if (sharedProperty.TEntityProperty.IsNullableReferenceType())
                {
                    continue;
                }

                var nestedBindings = GetNestedObjectBindings(sharedProperty, entityParameter, sharedBindings);
                var memberInit = CreateMemberInitExpression(sharedProperty.TResponseProperty.PropertyType, nestedBindings);
                sharedBindings.Add(Expression.Bind(sharedProperty.TResponseProperty, memberInit));
            }

            var responseObject = Expression.New(responseType);
            var responseObjectInit = Expression.MemberInit(responseObject, sharedBindings);
            return Expression.Lambda<Func<TEntity, TResponse>>(responseObjectInit, entityParameter);
        }

        private static List<MemberBinding> GetNestedObjectBindings(MappingProperty sharedProperty, ParameterExpression entityParameter, List<MemberBinding> parentBindings)
        {
            var entityNestedObjectExpression = Expression.Property(entityParameter, sharedProperty.TEntityProperty);
            var sharedNestedObjectProperties = GetSharedProperties(sharedProperty.TEntityProperty.PropertyType, sharedProperty.TResponseProperty.PropertyType);
            var entitySharedNestedObjectBindings = new List<MemberBinding>();

            foreach (var entitySharedNestedObjectProperty in sharedNestedObjectProperties)
            {
                if (primitiveTypes.Contains(entitySharedNestedObjectProperty.TResponseProperty.PropertyType))
                {
                    var entityNestedObjectProperty = Expression.Property(entityNestedObjectExpression, entitySharedNestedObjectProperty.TEntityProperty);
                    var binding = Expression.Bind(entitySharedNestedObjectProperty.TResponseProperty, entityNestedObjectProperty);
                    entitySharedNestedObjectBindings.Add(binding);
                    continue;
                }

                if (sharedProperty.TEntityProperty.PropertyType.IsEnum)
                {
                    var entityProperty = Expression.Property(entityParameter, sharedProperty.TEntityProperty);
                    var convert = Expression.Convert(entityProperty, sharedProperty.TResponseProperty.PropertyType);
                    var binding = Expression.Bind(sharedProperty.TResponseProperty, convert);
                    entitySharedNestedObjectBindings.Add(binding);
                    continue;
                }
                if (typeof(IEnumerable).IsAssignableFrom(sharedProperty.TEntityProperty.PropertyType))
                {
                    continue;
                }

                var nestedBindings = GetNestedObjectBindings(entitySharedNestedObjectProperty, entityParameter, entitySharedNestedObjectBindings);
                var memberInit = CreateMemberInitExpression(entitySharedNestedObjectProperty.TResponseProperty.PropertyType, nestedBindings);
                entitySharedNestedObjectBindings.Add(Expression.Bind(entitySharedNestedObjectProperty.TResponseProperty, memberInit));
            }

            return entitySharedNestedObjectBindings;
        }

        private static MemberInitExpression CreateMemberInitExpression(Type objectType, IEnumerable<MemberBinding> bindings)
        {
            var newObject = Expression.New(objectType);
            return Expression.MemberInit(newObject, bindings);
        }

        public static bool IsNullableReferenceType(this PropertyInfo property)
        {
            // Get the custom attributes of the property
            var customAttributes = property.GetCustomAttributesData();

            // Find the NullableAttribute
            return customAttributes.Any(a => a.AttributeType.Name == "NullableAttribute");
        }
    }
}
