using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;

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

        public record MappingProperties(PropertyInfo TResponseProperty, PropertyInfo TEntityProperty);

        private static IEnumerable<MappingProperties> GetSharedProperties(Type TEntityType, Type TResponseType)
        {
            return TResponseType.GetProperties()
                .Where(e => TEntityType.GetProperties().Any(a => a.Name == e.Name))
                .Where(e => primitiveTypes.Contains(e.PropertyType) || typeof(IEnumerable).IsAssignableFrom(e.PropertyType) == false)
                .Select(e =>
                {
                    return new MappingProperties(TResponseType.GetProperty(e.Name), TEntityType.GetProperty(e.Name));
                });
        }

        public static Expression<Func<TEntity, TResponse>> GenericSelectExpression<TEntity, TResponse>()
        {
            var tEntityType = typeof(TEntity);
            var tResponseType = typeof(TResponse);


            var sharedProperties = GetSharedProperties(tEntityType, tResponseType);
            var sharedParameter = Expression.Parameter(tEntityType, tEntityType.Name);

            var sharedBindings = sharedProperties.Select(sharedProperty =>
            {

                if (primitiveTypes.Contains(sharedProperty.TResponseProperty.PropertyType))
                {
                    return Expression.Bind(sharedProperty.TResponseProperty, Expression.Property(sharedParameter, sharedProperty.TEntityProperty));
                }

                if (typeof(IEnumerable).IsAssignableFrom(sharedProperty.TEntityProperty.PropertyType))
                {
                    throw new Exception($"GenericSelectExpression<{tEntityType.Name},{tResponseType.Name}> not Valid!");
                }

                var nestedObjectBingings = GetNestedObjectBindings(sharedProperty, sharedParameter);
                MemberInitExpression tResponseNestedObjectInit = CreateNestedMemberInitExpression(sharedProperty, sharedParameter, nestedObjectBingings);
                return Expression.Bind(sharedProperty.TResponseProperty, tResponseNestedObjectInit);
            });

            var tResponseObject = Expression.New(typeof(TResponse));
            var tResponseObjectInit = Expression.MemberInit(tResponseObject, sharedBindings);
            return Expression.Lambda<Func<TEntity, TResponse>>(tResponseObjectInit, sharedParameter);
        }

        private static MemberInitExpression CreateNestedMemberInitExpression(MappingProperties sharedProperty, ParameterExpression sharedParameter, IEnumerable<MemberAssignment> tEntitySharedNestedObjectBindings)
        {
            NewExpression tResponseNestedObject = Expression.New(sharedProperty.TResponseProperty.PropertyType);
            MemberInitExpression tResponseNestedObjectInit = Expression.MemberInit(tResponseNestedObject, tEntitySharedNestedObjectBindings);
            return tResponseNestedObjectInit;
        }

        private static IEnumerable<MemberAssignment> GetNestedObjectBindings(MappingProperties sharedProperty, ParameterExpression sharedParameter)
        {
            MemberExpression tEntityNestedObjectExpression = Expression.Property(sharedParameter, sharedProperty.TEntityProperty);
            var sharedNestedObjectProperties = GetSharedProperties(sharedProperty.TEntityProperty.PropertyType, sharedProperty.TResponseProperty.PropertyType);
            var tEntitySharedNestedObjectBindings = new List<MemberAssignment>();

            foreach (var tEntitySharedNestedObjectProperty in sharedNestedObjectProperties)
            {
                MemberExpression tEntityNestedObjectProperty = Expression.Property(tEntityNestedObjectExpression, tEntitySharedNestedObjectProperty.TEntityProperty);
                try
                {
                    tEntitySharedNestedObjectBindings.Add(Expression.Bind(tEntitySharedNestedObjectProperty.TResponseProperty, tEntityNestedObjectProperty));
                }
                catch (Exception ex)
                {
                    Console.WriteLine("EXCEPTION!");
                    //tEntitySharedNestedObjectBindings.Clear();
                    //var newParam = Expression.Parameter(sharedProperty.TEntityProperty.PropertyType, $"{sharedProperty.TEntityProperty.PropertyType.Name}");
                    //NewExpression tResponseNestedObject = Expression.New(sharedProperty.TEntityProperty.PropertyType);
                    //tEntitySharedNestedObjectBindings.AddRange(GetNestedObjectBindings(tEntitySharedNestedObjectProperty, newParam));
                }
            };
            return tEntitySharedNestedObjectBindings;
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