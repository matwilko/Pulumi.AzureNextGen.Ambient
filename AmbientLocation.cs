using System;
using System.Threading;

namespace Pulumi.AzureNextGen
{
	public sealed class AmbientLocation : IDisposable
	{
		private static AsyncLocal<AmbientLocation?> Ambient { get; } = new AsyncLocal<AmbientLocation?>();
		public static AmbientLocation? Current => Ambient.Value;

		public string Location { get; }

		public AmbientLocation(string location)
		{
			if (location == null)
				throw new ArgumentNullException(nameof(location));
			if (string.IsNullOrWhiteSpace(location))
				throw new ArgumentException("Location cannot be empty", nameof(location));

			if (Ambient.Value != null)
				throw new InvalidOperationException("There is already an ambient location in effect - nesting ambient locations is not supported");

			if (AmbientResourceGroup.Current != null)
				throw new InvalidOperationException("There is an ambient resource group in effect - nesting an ambient location inside an ambient resource group is not supported");

			Location = location;
			Ambient.Value = this;
		}

		internal static ResourceTransformation TransformDelegate { get; } = Transform;

		private static ResourceTransformationResult? Transform(ResourceTransformationArgs args)
		{
			var ambientLocation = Ambient.Value;
			if (ambientLocation == null)
				return null;

			if (!args.Resource.IsAzureNextGenResource() || args.Resource is Provider)
				return null;

			if (AmbientResourceGroup.Current != null)
			{
				Log.Debug("There is an ambient resource group in effect - that will set a Location if not already set");
				return null;
			}

			var locationProperty = args.Args.GetLocationProperty();
			if (locationProperty == null)
			{
				Log.Debug($"No Location property on resource args type `{args.Args.GetType()}`, so not setting the ambient location");
				return null;
			}

			var existingLocationValue = locationProperty.GetValue(args.Args);
			if (existingLocationValue != null)
			{
				Log.Debug("Location has already been manually specified on this resource, so the ambient location will not be applied");
				return null;
			}

			var clonedArgs = args.Args.Clone();
			locationProperty.SetInputValue(clonedArgs, ambientLocation.Location);
			return new ResourceTransformationResult(clonedArgs, args.Options);
		}

		private bool Disposed { get; set; }

		void IDisposable.Dispose()
		{
			if (Disposed)
				return;

			if (Ambient.Value == null)
				throw new InvalidOperationException("Tried to Dispose an AmbientLocation when there doesn't seem to be one - this state shouldn't be possible - please file a bug report with details of the code you wrote to get to this state?");

			if (Ambient.Value != this)
				throw new InvalidOperationException("Tried to Dispose an AmbientLocation, but this doesn't seem to be the current ambient location - this state shouldn't be possible - please file a bug report with details of the code you wrote to get to this state?");

			Ambient.Value = null;
			Disposed = true;
		}
	}
}