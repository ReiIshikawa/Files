// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Controls;
using Files.App.ViewModels.Properties;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.Graphics;
using Windows.System;

namespace Files.App.Views.Properties
{
	public sealed partial class MainPropertiesPage : BasePropertiesPage
	{
		private IAppThemeModeService AppThemeModeService { get; } = Ioc.Default.GetRequiredService<IAppThemeModeService>();

		private AppWindow AppWindow => Window.AppWindow;

		private Window Window = null!;

		private MainPropertiesViewModel MainPropertiesViewModel { get; set; } = null!;
		private AppWindow? _titleBarAppWindow;
		private XamlRoot? _titleBarXamlRoot;
		private double _lastTitleBarRasterizationScale;
		private bool _isTitleBarDragRegionUpdateQueued;
		private int _titleBarDragRegionSubscriptionGeneration;
		private bool _isWindowClosed;

		public MainPropertiesPage()
		{
			InitializeComponent();

			if (AppLanguageHelper.IsPreferredLanguageRtl)
				FlowDirection = FlowDirection.RightToLeft;

			Unloaded += Page_Unloaded;
		}


		// Navigates to specified properties page
		public bool TryNavigateToPage(PropertiesNavigationViewItemType pageType)
		{
			var page = MainPropertiesViewModel.NavigationItems.FirstOrDefault(item => item.ItemType == pageType);
			if (page is null)
				return false;

			MainPropertiesViewModel.SelectedNavigationItem = page;
			return true;
		}

		private void PropertiesSidebar_ItemInvoked(object sender, ItemInvokedEventArgs e)
		{
			if (sender is not SidebarItem { Item: PropertiesNavigationItem navItem })
				return;

			MainPropertiesViewModel.SelectedNavigationItem = navItem;
		}

		protected override void OnNavigatedTo(NavigationEventArgs e)
		{
			var parameter = (PropertiesPageNavigationParameter)e.Parameter;

			Window = parameter.Window!;

			base.OnNavigatedTo(e);

			MainPropertiesViewModel = new(Window, MainContentFrame, BaseProperties!, parameter);
		}

		private void Page_Loaded(object sender, RoutedEventArgs e)
		{
			_isWindowClosed = false;
			AppThemeModeService.AppThemeModeChanged -= AppThemeModeService_AppThemeModeChanged;
			AppThemeModeService.AppThemeModeChanged += AppThemeModeService_AppThemeModeChanged;
			Window.Closed -= Window_Closed;
			Window.Closed += Window_Closed;

			AppThemeModeService.ApplyResources();
			UpdatePageLayout(this.Width);
			PropertiesSidebar.SelectedItem = MainPropertiesViewModel.SelectedNavigationItem;
			SubscribeTitleBarDragRegionEvents();
			UpdateTitleBarDragRegion();
		}

		private void Page_Unloaded(object sender, RoutedEventArgs e)
		{
			AppThemeModeService.AppThemeModeChanged -= AppThemeModeService_AppThemeModeChanged;
			UnsubscribeTitleBarDragRegionEvents();
		}

		private int SetTitleBarDragRegion(InputNonClientPointerSource source, SizeInt32 size, double scaleFactor, Func<UIElement, RectInt32?, RectInt32> getScaledRect)
		{
			source.SetRegionRects(NonClientRegionKind.Passthrough, [getScaledRect(BackwardNavigationButton, null)]);
			return (int)TitlebarArea.ActualHeight;
		}

		private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
			=> UpdatePageLayout(e.NewSize.Width);

		private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
		{
			if (e.Key.Equals(VirtualKey.Escape))
				Window.Close();
		}

		private void UpdatePageLayout(double pageWidth)
		{
			VisualStateManager.GoToState(this, pageWidth < 600 ? "Narrow" : "Wide", true);
		}

		private async void AppThemeModeService_AppThemeModeChanged(object? sender, EventArgs e)
		{
			if (Parent is null)
				return;

			await DispatcherQueue.EnqueueOrInvokeAsync(() =>
			{
				AppThemeModeService.SetAppThemeMode(Window, Window.AppWindow.TitleBar, AppThemeModeService.AppThemeMode, false);
			});
		}

		private void Window_Closed(object sender, WindowEventArgs args)
		{
			_isWindowClosed = true;
			AppThemeModeService.AppThemeModeChanged -= AppThemeModeService_AppThemeModeChanged;
			Window.Closed -= Window_Closed;
			UnsubscribeTitleBarDragRegionEvents();

			if (MainPropertiesViewModel.ChangedPropertiesCancellationTokenSource is not null &&
				!MainPropertiesViewModel.ChangedPropertiesCancellationTokenSource.IsCancellationRequested)
			{
				MainPropertiesViewModel.ChangedPropertiesCancellationTokenSource.Cancel();
			}
		}

		private void SubscribeTitleBarDragRegionEvents()
		{
			UnsubscribeTitleBarDragRegionEvents();

			if (Window is not null)
			{
				try
				{
					var appWindow = Window.AppWindow;
					if (appWindow is not null)
					{
						_titleBarAppWindow = appWindow;
						appWindow.Changed += AppWindow_Changed;
					}
				}
				catch (Exception ex) when (DragZoneHelper.IsClosedObjectException(ex))
				{
				}
			}

			try
			{
				var xamlRoot = XamlRoot ?? Window?.Content?.XamlRoot;
				if (xamlRoot is not null)
				{
					_titleBarXamlRoot = xamlRoot;
					_lastTitleBarRasterizationScale = xamlRoot.RasterizationScale;
					xamlRoot.Changed += TitleBar_XamlRoot_Changed;
				}
			}
			catch (Exception ex) when (DragZoneHelper.IsClosedObjectException(ex))
			{
			}

			TitlebarArea.SizeChanged += TitleBarElement_SizeChanged;
			BackwardNavigationButton.SizeChanged += TitleBarElement_SizeChanged;
		}

		private void UnsubscribeTitleBarDragRegionEvents()
		{
			if (_titleBarAppWindow is not null)
			{
				try
				{
					_titleBarAppWindow.Changed -= AppWindow_Changed;
				}
				catch (Exception ex) when (DragZoneHelper.IsClosedObjectException(ex))
				{
				}

				_titleBarAppWindow = null;
			}

			if (_titleBarXamlRoot is not null)
			{
				try
				{
					_titleBarXamlRoot.Changed -= TitleBar_XamlRoot_Changed;
				}
				catch (Exception ex) when (DragZoneHelper.IsClosedObjectException(ex))
				{
				}

				_titleBarXamlRoot = null;
			}

			TitlebarArea.SizeChanged -= TitleBarElement_SizeChanged;
			BackwardNavigationButton.SizeChanged -= TitleBarElement_SizeChanged;
			_titleBarDragRegionSubscriptionGeneration++;
			_isTitleBarDragRegionUpdateQueued = false;
		}

		private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs e)
		{
			if (DragZoneHelper.ShouldRefreshTitleBarDragRegion(sender, e))
				QueueTitleBarDragRegionUpdate();
		}

		private void TitleBar_XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
		{
			double scale;
			try
			{
				scale = sender.RasterizationScale;
			}
			catch (Exception ex) when (DragZoneHelper.IsClosedObjectException(ex))
			{
				return;
			}

			if (scale == _lastTitleBarRasterizationScale)
				return;

			_lastTitleBarRasterizationScale = scale;
			QueueTitleBarDragRegionUpdate();
		}

		private void TitleBarElement_SizeChanged(object sender, SizeChangedEventArgs e)
			=> QueueTitleBarDragRegionUpdate();

		private void QueueTitleBarDragRegionUpdate()
		{
			if (_isTitleBarDragRegionUpdateQueued || _isWindowClosed || !IsLoaded)
				return;

			var generation = _titleBarDragRegionSubscriptionGeneration;
			_isTitleBarDragRegionUpdateQueued = true;
			if (!DispatcherQueue.TryEnqueue(() => UpdateTitleBarDragRegionFromQueue(generation)))
				_isTitleBarDragRegionUpdateQueued = false;
		}

		private void UpdateTitleBarDragRegionFromQueue(int generation)
		{
			if (generation != _titleBarDragRegionSubscriptionGeneration)
				return;

			_isTitleBarDragRegionUpdateQueued = false;
			UpdateTitleBarDragRegion();
		}

		private void UpdateTitleBarDragRegion()
		{
			if (_isWindowClosed || !IsLoaded || Window is null)
				return;

			try
			{
				if (Window.AppWindow is not { IsVisible: true })
					return;

				if (_titleBarXamlRoot is not null)
					_lastTitleBarRasterizationScale = _titleBarXamlRoot.RasterizationScale;

				Window.RaiseSetTitleBarDragRegion(SetTitleBarDragRegion);
			}
			catch (Exception ex) when (DragZoneHelper.IsClosedObjectException(ex))
			{
			}
		}

		public override async Task<bool> SaveChangesAsync()
			=> await Task.FromResult(false);

		public override void Dispose()
		{
		}
	}
}
