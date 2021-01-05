using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Pulumi.AzureNextGen
{
	internal static class Reflection
	{
		private static ImmutableHashSet<Type> AzureNextGenResourceTypes { get; } = typeof(Provider).Assembly.ExportedTypes
			.Where(t => typeof(Resource).IsAssignableFrom(t))
			.ToImmutableHashSet();

		public static bool IsAzureNextGenResource(this Resource resource) => AzureNextGenResourceTypes.Contains(resource.GetType());

		private static ImmutableDictionary<Type, PropertyInfo> ResourceGroupProperties { get; } = typeof(Provider).Assembly.ExportedTypes
			.Where(t => typeof(ResourceArgs).IsAssignableFrom(t))
			.Select(t => (type: t, property: t.GetProperty("ResourceGroupName")))
			.Where(info => info.property != null)
			.ToImmutableDictionary(i => i.type, i => i.property);

		public static PropertyInfo GetResourceGroupProperty(this ResourceArgs args) => ResourceGroupProperties.GetValueOrDefault(args.GetType());

		private static ImmutableDictionary<Type, PropertyInfo> LocationProperties { get; } = typeof(Provider).Assembly.ExportedTypes
			.Where(t => typeof(ResourceArgs).IsAssignableFrom(t))
			.Select(t => (type: t, property: t.GetProperty("Location")))
			.Where(info => info.property != null)
			.ToImmutableDictionary(i => i.type, i => i.property);

		public static PropertyInfo GetLocationProperty(this ResourceArgs args) => LocationProperties.GetValueOrDefault(args.GetType());

		private static ConcurrentDictionary<Type, Func<ResourceArgs, ResourceArgs>> Cloners { get; } = new ConcurrentDictionary<Type, Func<ResourceArgs, ResourceArgs>>();
		private static Func<ResourceArgs, ResourceArgs> CreateResourceArgsCloner(Type t)
		{
			var resourceArgs = Expression.Parameter(typeof(ResourceArgs), "resourceArgs");
			var typedResourceArgs = Expression.Variable(t, "typedResourceArgs");
			var clonedArgs = Expression.Variable(t, "clonedArgs");

			var block = new List<Expression>();
			block.Add(Expression.Assign(typedResourceArgs, Expression.Convert(resourceArgs, t)));
			block.Add(Expression.Assign(clonedArgs, Expression.New(t)));

			// Note: only doing a shallow clone here because we only ever modify properties containing immutable values
			foreach (var property in t.GetProperties().Where(p => p.CanRead && p.CanWrite))
				block.Add(Expression.Assign(Expression.Property(clonedArgs, property), Expression.Property(typedResourceArgs, property)));

			block.Add(Expression.Convert(clonedArgs, typeof(ResourceArgs)));

			var blockExpression = Expression.Block(typeof(ResourceArgs), new[] { typedResourceArgs, clonedArgs }, block);

			return Expression
				.Lambda<Func<ResourceArgs, ResourceArgs>>(blockExpression, $"Clone{t.Name}", new[] {resourceArgs})
				.Compile();
		}

		private static Func<ResourceOptions, ResourceOptions> CloneOptionsMethod { get; } = (Func<ResourceOptions, ResourceOptions>) typeof(ResourceOptions)
				.GetMethod("Clone", BindingFlags.Instance | BindingFlags.NonPublic)!
			.CreateDelegate(typeof(Func<ResourceOptions, ResourceOptions>));

		public static ResourceArgs Clone(this ResourceArgs args)
		{
			var cloner = Cloners.GetOrAdd(args.GetType(), t => CreateResourceArgsCloner(t));
			return cloner(args);
		}

		public static ResourceOptions CloneFor(this ResourceOptions? options, Resource resource)
		{
			if (options != null)
				return CloneOptionsMethod(options);
			else if (resource is CustomResource)
				return new CustomResourceOptions();
			else if (resource is ComponentResource)
				return new ComponentResourceOptions();
			else
				throw new InvalidOperationException($"Not sure what type of options to create for the resource `{resource.GetResourceName()}`");
		}

		private static ImmutableDictionary<Type, PropertyInfo> ResourceNameProperties { get; } = typeof(Provider).Assembly.ExportedTypes
			.Where(t => typeof(Resource).IsAssignableFrom(t))
			.SelectMany(t => t.GetConstructors())
			.SelectMany(c => c.GetParameters())
			.Select(p => p.ParameterType)
			.Where(t => typeof(ResourceArgs).IsAssignableFrom(t))
			.Distinct()
			.Select(t => (type: t, resourceNameProp: TryGetResourceNameProperty(t)))
			.Where(t => t.resourceNameProp != null)
			.ToImmutableDictionary(t => t.type, t => t.resourceNameProp);

		private static PropertyInfo? TryGetResourceNameProperty(Type type)
		{
			var nameProperties = type.GetProperties()
				.Where(p => !p.Name.Equals("ResourceGroupName", StringComparison.OrdinalIgnoreCase))
				.Where(p => p.Name.EndsWith("Name", StringComparison.OrdinalIgnoreCase))
				.ToArray();

			if (nameProperties.Length == 1)
				return nameProperties.Single();

			var sameNamedProp = nameProperties.SingleOrDefault(p => p.Name.Equals(type.Name[0..^4] + "Name", StringComparison.OrdinalIgnoreCase));
			if (sameNamedProp != null)
				return sameNamedProp;

			var explicitNameProp = nameProperties.SingleOrDefault(p => p.Name.Equals("Name", StringComparison.OrdinalIgnoreCase));
			if (explicitNameProp != null)
				return explicitNameProp;

			var substringNamedProp = nameProperties
				.Where(p => !p.Name.Equals("Name", StringComparison.OrdinalIgnoreCase))
				.SingleOrDefault(p => type.Name.EndsWith(p.Name[0..^4] + "Args", StringComparison.OrdinalIgnoreCase));
			if (substringNamedProp != null)
				return substringNamedProp;

			return null;
		}

		public static PropertyInfo? GetNameProperty(this ResourceArgs args) => ResourceNameProperties.GetValueOrDefault(args.GetType());

		private static ConcurrentDictionary<PropertyInfo, Func<object, object>> PropertyInputConverters { get; } = new ConcurrentDictionary<PropertyInfo, Func<object, object>>();

		public static void SetInputValue(this PropertyInfo property, object instance, object value)
		{
			var converter = PropertyInputConverters.GetOrAdd(property, p => CreateConverter(p));

			var inputValue = converter(value);

			property.SetValue(instance, inputValue);

			static Func<object, object> CreateConverter(PropertyInfo property)
			{
				var inputType = property.PropertyType;
				if (inputType.GetGenericTypeDefinition() != typeof(Input<>))
					throw new InvalidOperationException("SetInputValue can only be called on properties of type Input<T>");

				var inputGeneric = inputType.GetGenericArguments().Single();

				Func<object, object> wrongDelegate = ConvertToInputValue<object>;
				var genericMethod = wrongDelegate.Method.GetGenericMethodDefinition();

				return (Func<object, object>) genericMethod.MakeGenericMethod(inputGeneric).CreateDelegate(typeof(Func<object, object>));
			}

			static object ConvertToInputValue<T>(object value)
			{
				return value switch
				{
					Input<T> i => i,
					Output<T> o => (Input<T>) o,
					T t => (Input<T>) t,
					_ => throw new InvalidOperationException($"Could not convert the value to the necessary Input<{typeof(T).Name}> - `{value.GetType().Name}` is not compatible")
				};
			}
		}
	}
}