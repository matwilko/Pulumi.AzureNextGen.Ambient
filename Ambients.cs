namespace Pulumi.AzureNextGen
{
	public static class Ambients
	{
		public static StackOptions Setup(bool disableSubscription = false, bool disableResourceGroup = false, bool disableLocation = false, bool disableAutoNaming = false, StackOptions? options = null)
		{
			options ??= new StackOptions();

			if (!disableSubscription)
				options.ResourceTransformations.Add(AmbientSubscription.TransformDelegate);
			if (!disableResourceGroup)
				options.ResourceTransformations.Add(AmbientResourceGroup.TransformDelegate);
			if (!disableLocation)
				options.ResourceTransformations.Add(AmbientLocation.TransformDelegate);
			if (!disableAutoNaming)
				options.ResourceTransformations.Add(AutoNaming.TransformDelegate);

			return options;
		}
	}
}