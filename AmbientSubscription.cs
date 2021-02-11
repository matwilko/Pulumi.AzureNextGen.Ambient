using System;
using System.Collections.Generic;
using System.Threading;

namespace Pulumi.AzureNextGen
{
	public sealed class AmbientSubscription : IDisposable
	{
		private static AsyncLocal<AmbientSubscription?> Ambient { get; } = new AsyncLocal<AmbientSubscription?>();
		private static Dictionary<string, Provider> ProviderCache { get; } = new Dictionary<string, Provider>();

		private string SubscriptionId { get; }
		private Provider AzureNextGenProvider { get; }

		public static AmbientSubscription? Current => Ambient.Value;

		public AmbientSubscription(string subscriptionId)
		{
			if (subscriptionId == null)
				throw new ArgumentNullException(nameof(subscriptionId));

			if (string.IsNullOrWhiteSpace(subscriptionId))
				throw new ArgumentException("subscriptionId cannot be empty", nameof(subscriptionId));

			if (Ambient.Value != null)
				throw new InvalidOperationException("There is already an ambient subscription in effect - nesting ambient subscriptions is not supported, you must close the previous subscription before trying to switch to a new one");

			if (AmbientResourceGroup.Current != null)
				throw new InvalidOperationException("There is an ambient resource group in effect - nesting an ambient subscription inside an ambient resource group is not supported");

			SubscriptionId = subscriptionId;
			lock (ProviderCache)
			{
				AzureNextGenProvider = ProviderCache.TryGetValue(subscriptionId, out var provider)
					? provider
					: ProviderCache[subscriptionId] = new Provider($"azurenextgen-ambientsubscription-{subscriptionId}", new ProviderArgs { SubscriptionId = SubscriptionId });
			}

			Ambient.Value = this;
		}

		internal static ResourceTransformation TransformDelegate { get; } = Transform;

		private static ResourceTransformationResult? Transform(ResourceTransformationArgs args)
		{
			var ambientSubscription = Ambient.Value;
			if (ambientSubscription == null)
				return null;

			if (!args.Resource.IsAzureNextGenResource() || args.Resource is Provider)
				return null;
			
			if (args.Options?.Provider != null)
			{
				Log.Debug($"The Ambient Subscription `{ambientSubscription.SubscriptionId}` could not be applied as this resource already has a provider applied to it");
				return null;
			}
			
			var options = args.Options.CloneFor(args.Resource);
			options.Provider = ambientSubscription.AzureNextGenProvider;
			return new ResourceTransformationResult(args.Args, options);
		}

		private bool Disposed { get; set; }

		void IDisposable.Dispose()
		{
			if (Disposed)
				return;

			if (Ambient.Value == null)
				throw new InvalidOperationException("Tried to Dispose an AmbientSubscription when there doesn't seem to be one - this state shouldn't be possible - please file a bug report with details of the code you wrote to get to this state?");

			if (Ambient.Value != this)
				throw new InvalidOperationException("Tried to Dispose an AmbientSubscription, but this doesn't seem to be the current ambient subscription - this state shouldn't be possible - please file a bug report with details of the code you wrote to get to this state?");

			Ambient.Value = null;
			Disposed = true;
		}
	}
}