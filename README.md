# Pulumi.AzureNextGen.Ambient
Provides some helpers to allow specifying subscription/resource group for multiple resources easier

## Setup

### With all ambient features
```csharp

class YourStack : Stack
{
	public YourStack() : base(Ambients.Setup())
	{
		// ...
	}
}
```

### With some disabled features
```csharp

class YourStack : Stack
{
	public YourStack() : base(Ambients.Setup(disableAutoNaming: true, disableLocation: true))
	{
		// ...
	}
}

```

### With custom StackOptions and disabled features
```csharp

class YourStack : Stack
{
	public YourStack() : base(Ambients.Setup(disableSubscription: true, new StackOptions { ... }))
	{
		// ...
	}
}

```

## Usage

### Auto-naming

All resources that support it will have their relevant `*Name` property set to the name of the resource, to avoid having to set the name in the constructor and the args.

```csharp

var resourceGroup = new ResourceGroup("a-new-resource-group", new ResourceGroupArgs { Location = "SouthCentralUS" });

```

The `ResourceGroupName` will be set to `a-new-resource-group`, eliminating the need to set it manually.


### Ambients

Subscriptions, locations and resource groups can all be made "ambient", that is, within a certain scope, all resources will be setup to be in the ambient subscription/location/resource group (if they do not manually set one).

It's generally advised to make use of `using` blocks to make these scopes easy to reason about - this library was designed around this usage!

#### Ambient Subscription

When an AmbientSubscription is set, a new `Pulumi.AzureNextGen.Provider` will be created with the SubscriptionId set to the provided subscription. Providers are cached so that if there are multiple blocks specifying the same subscription ID, only one provider is created.

Note that if a custom provider is used on a resource, the ambient subscription cannot be applied.

You cannot nest an ambient subscription inside an ambient resource group.

```csharp

using (new AmbientSubscription("93e6beff-01b3-4d72-96ac-189afbf62f65"))
{
	// All resources specified within this using block will be placed in this subscription
	var resourceGroup = new ResourceGroup(...);
	// ..
}

using (new AmbientSubscription("06b6c8ff-01fc-438f-9dde-128f52b21fa7"))
{
	// A new provider instance is applied to resources in this block

	// This resource won't have the ambient subscription applied to it!
	var rg = new ResourceGroup(..., new CustomResourceOptions { Provider = ... } );
}

using (new AmbientSubscription("93e6beff-01b3-4d72-96ac-189afbf62f65"))
{
	// Applies the same Provider instance to resources in this block as the first block
	var resourceGroup = new ResourceGroup(...);
	// ..
}

```

#### Ambient location

When an ambient location is set, the `Location` property of each resource's `*Args` will be set if it is not already present.

The ambient location will also not apply if an ambient resource group is in effect - usually the ambient location will be applied to the ambient resource group anyway.

You cannot nest an ambient location inside an ambient resource group.

```csharp

using (new AmbientLocation("SouthCentralUS"))
{
	// This resource group will have Location = SouthCentralUS
	var resourceGroup = new ResourceGroup(...);

	// This resource group will have Location = WestEu - the ambient location does not override manual values
	var resourceGroup = new ResourceGroup(..., new ResourceGroupArgs { Location = "WestEu" });
}

```

#### Ambient ResourceGroup

When an ambient resource group is set, both the `ResourceGroupName` and `Location` property of any resource's `*Args` are set (if present).

Note that if _either_ the `ResourceGroupName` or the `Location` is set manually, then _both_ must be set manually - the ambient resource group is all or nothing to avoid potentially counter-intuitive behaviour.

`AmbientResourceGroup` provides some shorthand constructors to make defining simple resource groups, with just a name or a name and location. You can also manually create your resource group, and then pass it to make it the ambient resource group.

You cannot nest other ambients inside an ambient resource group.

```csharp

using (new AmbientResourceGroup("some-resource-group", "SouthCentralUS"))
{
	// All resources in this block will be in `some-resource-group`
}

using (new AmbientLocation("WestEU"))
{
	// When using the constructor only taking a name, you'll need an ambient location set so the resource group can inherit it

	using (new AmbientResourceGroup("resource-group-two"))
	{
		// ...
	}

	using (new AmbientResourceGroup("resource-group-three"))
	{
		// ...
	}
}

var customResourceGroup = new ResourceGroup(...);
using (new AmbientResourceGroup(customResourceGroup))
{
	// ...
}

```

