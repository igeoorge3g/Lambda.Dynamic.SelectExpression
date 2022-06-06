using System.Linq.Expressions;
using System.Reflection;

namespace Lambda.Dynamic.SelectExpression
{
    public static class LabmdaDynamicSelectExpressions
    {
        private static readonly Type[] classTypes = new[] { typeof(string), typeof(decimal), typeof(DateTime), typeof(Guid) };
        public record MappingProperties(PropertyInfo TResponseProperty, PropertyInfo TEntityProperty);

        private static IEnumerable<MappingProperties> GetSharedProperties(Type TEntityType, Type TResponseType)
        {
            return TResponseType.GetProperties()
                .Where(e => TEntityType.GetProperties().Any(a => a.Name == e.Name))
                .Select(e =>
                {
                    return new MappingProperties(TResponseType.GetProperty(e.Name), TEntityType.GetProperty(e.Name));
                });
        }

        public static Expression<Func<TEntity, TResponse>> GenericSelectExpression<TEntity, TResponse>()
        {
            var sharedProperties = GetSharedProperties(typeof(TEntity), typeof(TResponse));
            var sharedParameter = Expression.Parameter(typeof(TEntity), typeof(TEntity).Name);
            var sharedBindings = sharedProperties.Select(sharedProperty =>
            {
                if (!classTypes.Contains(sharedProperty.TResponseProperty.PropertyType) && !sharedProperty.TResponseProperty.PropertyType.IsClass)
                {
                    //TResponse.Property,TEntity.Propery
                    return Expression.Bind(sharedProperty.TResponseProperty, Expression.Property(sharedParameter, sharedProperty.TEntityProperty));
                }

                //TEntity => TEntity.ParentProperty
                MemberExpression tEntityNestedObjectExpression = Expression.Property(sharedParameter, sharedProperty.TEntityProperty);

                //Reference => TEntity.Parent (Object Property)
                var tResponseNestedObjectProperties = sharedProperty.TResponseProperty.PropertyType.GetProperties();
                var tEntityNestedObjectProperties = sharedProperty.TEntityProperty.PropertyType.GetProperties();
                var tEntitySharedNestedObjectProperties = tEntityNestedObjectProperties.Where(e => tResponseNestedObjectProperties.Any(a => a.Name == e.Name));
                var sharedNestedObjectProperties = GetSharedProperties(sharedProperty.TEntityProperty.PropertyType, sharedProperty.TResponseProperty.PropertyType);

                var tEntitySharedNestedObjectBindings = sharedNestedObjectProperties
                .Select(tEntitySharedNestedObjectProperty =>
                {
                    MemberExpression tEntityNestedObjectProperty = Expression.Property(tEntityNestedObjectExpression, tEntitySharedNestedObjectProperty.TEntityProperty);
                    //TResponse.Id,TEntity.Id
                    return Expression.Bind(tEntitySharedNestedObjectProperty.TResponseProperty, tEntityNestedObjectProperty);
                });
                // new TResponse()
                NewExpression tResponseNestedObject = Expression.New(sharedProperty.TResponseProperty.PropertyType);
                // new TResponse() { Id = i.Id, Name = i.Name }
                MemberInitExpression tResponseNestedObjectInit = Expression.MemberInit(tResponseNestedObject, tEntitySharedNestedObjectBindings);

                return Expression.Bind(sharedProperty.TResponseProperty, tResponseNestedObjectInit);
            });

            // new TResponse()
            var tResponseObject = Expression.New(typeof(TResponse));
            // new TResponse() { Id = i.Id, Name = i.Name }
            var tResponseObjectInit = Expression.MemberInit(tResponseObject, sharedBindings);
            // i => new TResponse() { Id = i.Id, Name = i.Name };
            return Expression.Lambda<Func<TEntity, TResponse>>(tResponseObjectInit, sharedParameter);
        }

    }
}