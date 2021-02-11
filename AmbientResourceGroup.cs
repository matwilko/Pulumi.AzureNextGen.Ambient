using System;
using System.Threading;
using Pulumi.AzureNextGen.Resources.Latest;

namespace Pulumi.AzureNextGen
{
	public sealed class AmbientResourceGroup : IDisposable
	{
		private static AsyncLocal<AmbientResourceGroup?> Ambient { get; } = new AsyncLocal<AmbientResourceGroup?>();
		public static AmbientResourceGroup? Current => Ambient.Value;

		public ResourceGroup ResourceGroup { get; }

		public AmbientResourceGroup(string name, string location)
			: this(new ResourceGroup(name, new ResourceGroupArgs { ResourceGroupName = name, Location = location }))
		{
		}

		public AmbientResourceGroup(string name)
			: this(new ResourceGroup(name, new ResourceGroupArgs { ResourceGroupName = name }))
		{
		}

		public AmbientResourceGroup(ResourceGroup resourceGroup)
		{
			if (resourceGroup == null)
				throw new ArgumentNullException(nameof(resourceGroup));

			if (Ambient.Value != null)
				throw new InvalidOperationException("There is already an ambient resource group in effect - nesting ambient resource groups is not supported, you must close the previous resource group before trying to switch to a new one");

			ResourceGroup = resourceGroup;
			Ambient.Value = this;
		}

		internal static ResourceTransformation TransformDelegate { get; } = Transform;

		private static ResourceTransformationResult? Transform(ResourceTransformationArgs args)
		{
			var ambientResourceGroup = Ambient.Value;
			if (ambientResourceGroup == null)
				return null;

			if (!args.Resource.IsAzureNextGenResource() || args.Resource is Provider || args.Resource is ResourceGroup)
				return null;

			var resourceGroupProperty = args.Args.GetResourceGroupProperty();
			var locationProperty = args.Args.GetLocationProperty();

			var existingResourceGroupName = resourceGroupProperty?.GetValue(args.Args);
			var existingLocation = locationProperty?.GetValue(args.Args);

			if (existingResourceGroupName != null && existingLocation != null)
			{
				Log.Debug("Both ResourceGroupName and Location have been manually specified on this resource, so the ambient resource group will not be applied");
				return null;
			}
			else if (existingResourceGroupName != null)
			{
				Log.Debug("ResourceGroupName has already been manually specified on this resource, so the ambient resource group will not be applied");
				return null;
			}
			else if (existingLocation != null)
			{
				Log.Debug("Location has already been manually specified on this resource, so the ambient resource group will not be applied");
				return null;
			}

			var clonedArgs = args.Args.Clone();
			resourceGroupProperty?.SetInputValue(clonedArgs, ambientResourceGroup.ResourceGroup.Name);
			locationProperty?.SetInputValue(clonedArgs, ambientResourceGroup.ResourceGroup.Location);

			return new ResourceTransformationResult(clonedArgs, args.Options);
		}

		private bool Disposed { get; set; }

		void IDisposable.Dispose()
		{
			if (Disposed)
				return;

			if (Ambient.Value == null)
				throw new InvalidOperationException("Tried to Dispose an AmbientResourceGroup when there doesn't seem to be one - this state shouldn't be possible - please file a bug report with details of the code you wrote to get to this state?");

			if (Ambient.Value != this)
				throw new InvalidOperationException("Tried to Dispose an AmbientResourceGroup, but this doesn't seem to be the current resource group - this state shouldn't be possible - please file a bug report with details of the code you wrote to get to this state?");

			Ambient.Value = null;
			Disposed = true;
		}
	}
}