namespace Pulumi.AzureNextGen
{
	internal sealed class AutoNaming
	{
		internal static ResourceTransformation TransformDelegate { get; } = Transform;

		private static ResourceTransformationResult? Transform(ResourceTransformationArgs args)
		{
			if (!args.Resource.IsAzureNextGenResource() || args.Resource is Provider)
				return null;

			var nameProperty = args.Args.GetNameProperty();

			if (nameProperty == null)
			{
				Log.Debug($"Not applying auto-naming - could not find a matching name property for the resource args type `{args.Args.GetType()}`");
				return null;
			}

			var existingName = nameProperty.GetValue(args.Args);
			if (existingName != null)
			{
				Log.Info("Not applying auto-naming - a name is already manually specified");
				return null;
			}

			var clonedArgs = args.Args.Clone();
			nameProperty.SetInputValue(clonedArgs, args.Resource.GetResourceName());

			return new ResourceTransformationResult(clonedArgs, args.Options);
		}
	}
}
