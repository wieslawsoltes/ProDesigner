namespace ProDesigner.Workbench;

public static class Samples
{
    public static IReadOnlyDictionary<string, string> Documents { get; } = new Dictionary<string, string>
    {
        ["Dashboard.axaml"] = Dashboard, ["SignIn.axaml"] = SignIn, ["Settings.axaml"] = Settings
    };
    public const string Dashboard = """
<UserControl xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#F5F6FA">
  <Grid ColumnDefinitions="176,*" RowDefinitions="64,*">
    <Border Grid.RowSpan="2" Background="#1C2030" Padding="22,26">
      <StackPanel Spacing="28">
        <TextBlock Text="◈  orbit" FontSize="26" FontWeight="Bold" Foreground="#FFFFFF" />
        <TextBlock Text="WORKSPACE" FontSize="10" Foreground="#8B93AB" Margin="0,22,0,0" />
        <Border Background="#36334F" CornerRadius="8" Padding="12"><TextBlock Text="▦  Overview" Foreground="#C2B6FF" /></Border>
        <TextBlock Text="↗  Analytics" Foreground="#A7AEC2" Margin="12,0" />
        <TextBlock Text="▤  Projects" Foreground="#A7AEC2" Margin="12,0" />
        <TextBlock Text="◷  Activity" Foreground="#A7AEC2" Margin="12,0" />
        <TextBlock Text="⚙  Settings" Foreground="#A7AEC2" Margin="12,0" />
        <Border Background="#292E42" CornerRadius="12" Padding="14" Margin="0,80,0,0">
          <StackPanel Spacing="10"><TextBlock Text="Make room for more" Foreground="White" FontSize="12" /><TextBlock Text="Your next idea starts here." Foreground="#9FA7BC" FontSize="10" TextWrapping="Wrap" /><Button Content="Explore Pro  ↗" FontSize="11" Background="#B3A1FF" Foreground="#191525" HorizontalAlignment="Stretch" /></StackPanel>
        </Border>
      </StackPanel>
    </Border>
    <Border Grid.Column="1" Background="White" BorderBrush="#E5E7EE" BorderThickness="0,0,0,1" Padding="26,16">
      <Grid ColumnDefinitions="*,Auto"><TextBlock Text="Workspace  /  Overview" Foreground="#777E91" VerticalAlignment="Center" FontSize="12" /><TextBlock Grid.Column="1" Text="⌕ Search     ◌     AM" Foreground="#545D73" VerticalAlignment="Center" FontSize="12" /></Grid>
    </Border>
    <Canvas x:Name="DashboardCanvas" Grid.Column="1" Grid.Row="1" Background="Transparent">
      <TextBlock x:Name="Greeting" Canvas.Left="28" Canvas.Top="28" Text="Good morning, Alex ✦" FontSize="27" FontWeight="SemiBold" Foreground="#20263A" />
      <TextBlock Canvas.Left="28" Canvas.Top="70" Text="A little clarity for your next big move." FontSize="12" Foreground="#9298A7" />
      <Button x:Name="CreateProject" Canvas.Left="572" Canvas.Top="32" Content="＋ New project" Background="#8570D8" Foreground="White" Padding="16,10" CornerRadius="7" />
      <Border x:Name="RevenueCard" Canvas.Left="28" Canvas.Top="112" Width="220" Height="132" Background="White" CornerRadius="12" Padding="20" BorderBrush="#E6E8EF" BorderThickness="1">
        <StackPanel Spacing="12"><TextBlock Text="Total revenue       ↗" Foreground="#8890A2" FontSize="12" /><TextBlock Text="$48,295" FontSize="30" FontWeight="SemiBold" Foreground="#232B40" /><TextBlock Text="↗ 18.4%  vs. last month" Foreground="#399A76" FontSize="11" /></StackPanel>
      </Border>
      <Border x:Name="ProjectsCard" Canvas.Left="264" Canvas.Top="112" Width="220" Height="132" Background="White" CornerRadius="12" Padding="20" BorderBrush="#E6E8EF" BorderThickness="1">
        <StackPanel Spacing="12"><TextBlock Text="Active projects       ◈" Foreground="#8890A2" FontSize="12" /><TextBlock Text="24" FontSize="30" FontWeight="SemiBold" Foreground="#232B40" /><TextBlock Text="6 ready to take flight" Foreground="#8B78C4" FontSize="11" /></StackPanel>
      </Border>
      <Border x:Name="CompletionCard" Canvas.Left="500" Canvas.Top="112" Width="220" Height="132" Background="#EBE6FB" CornerRadius="12" Padding="20">
        <StackPanel Spacing="12"><TextBlock Text="Completion rate       ◎" Foreground="#7C6F9F" FontSize="12" /><TextBlock Text="92.6%" FontSize="30" FontWeight="SemiBold" Foreground="#4C3E76" /><TextBlock Text="Your best month yet." Foreground="#8B78C4" FontSize="11" /></StackPanel>
      </Border>
      <Border x:Name="ActivityCard" Canvas.Left="28" Canvas.Top="264" Width="456" Height="244" Background="White" CornerRadius="12" Padding="22" BorderBrush="#E6E8EF" BorderThickness="1">
        <Grid RowDefinitions="Auto,Auto,*,Auto"><TextBlock Text="Activity overview" FontSize="15" FontWeight="SemiBold" Foreground="#293146" /><TextBlock Grid.Row="1" Text="Steady progress, meaningful results." FontSize="11" Foreground="#9A9FAE" Margin="0,8,0,0" />
          <StackPanel Grid.Row="2" Orientation="Horizontal" Spacing="16" VerticalAlignment="Bottom" Margin="8,18,0,12">
            <Rectangle Width="30" Height="38" Fill="#DDD5F8" RadiusX="4" RadiusY="4" VerticalAlignment="Bottom" /><Rectangle Width="30" Height="60" Fill="#D0C5F3" RadiusX="4" RadiusY="4" VerticalAlignment="Bottom" /><Rectangle Width="30" Height="50" Fill="#BCABEB" RadiusX="4" RadiusY="4" VerticalAlignment="Bottom" /><Rectangle Width="30" Height="90" Fill="#A28BDF" RadiusX="4" RadiusY="4" VerticalAlignment="Bottom" /><Rectangle Width="30" Height="72" Fill="#BCAAEA" RadiusX="4" RadiusY="4" VerticalAlignment="Bottom" /><Rectangle Width="30" Height="110" Fill="#8770CE" RadiusX="4" RadiusY="4" VerticalAlignment="Bottom" /><Rectangle Width="30" Height="98" Fill="#A18ADB" RadiusX="4" RadiusY="4" VerticalAlignment="Bottom" /><Rectangle Width="30" Height="132" Fill="#7054BB" RadiusX="4" RadiusY="4" VerticalAlignment="Bottom" />
          </StackPanel><TextBlock Grid.Row="3" Text="JAN       FEB       MAR       APR       MAY       JUN       JUL       AUG" FontSize="9" Foreground="#A5A9B5" />
        </Grid>
      </Border>
      <Border x:Name="TeamCard" Canvas.Left="500" Canvas.Top="264" Width="220" Height="244" Background="White" CornerRadius="12" Padding="20" BorderBrush="#E6E8EF" BorderThickness="1">
        <StackPanel Spacing="18"><TextBlock Text="The people behind it" FontSize="14" FontWeight="SemiBold" Foreground="#293146" /><TextBlock Text="◉  Maya Chen     Design" FontSize="11" Foreground="#788096" /><TextBlock Text="◉  Oliver Park   Product" FontSize="11" Foreground="#788096" /><TextBlock Text="◉  Sam Rivera    Develop" FontSize="11" Foreground="#788096" /><Separator /><TextBlock Text="＋ Invite a teammate" FontSize="11" Foreground="#8B72CC" /></StackPanel>
      </Border>
    </Canvas>
  </Grid>
</UserControl>
""";
    public const string SignIn = """
<UserControl xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#F4F1FC">
  <Grid>
    <Border x:Name="SignInCard" MaxWidth="370" Margin="24" Padding="32" Background="White" CornerRadius="20" VerticalAlignment="Center">
      <StackPanel Spacing="18">
        <TextBlock Text="◈" FontSize="42" Foreground="#8C73DA" HorizontalAlignment="Center" />
        <TextBlock Text="Welcome back" FontSize="28" FontWeight="SemiBold" Foreground="#252C40" HorizontalAlignment="Center" />
        <TextBlock Text="A space for your best work." FontSize="13" Foreground="#959BAB" HorizontalAlignment="Center" />
        <TextBlock Text="Email address" FontSize="12" Foreground="#596176" Margin="0,12,0,0" />
        <TextBox x:Name="EmailInput" Watermark="you@company.com" />
        <TextBlock Text="Password" FontSize="12" Foreground="#596176" />
        <TextBox x:Name="PasswordInput" Watermark="Enter your password" />
        <CheckBox Content="Keep me signed in" FontSize="12" />
        <Button x:Name="SignInButton" Content="Sign in  →" Background="#8870D3" Foreground="White" HorizontalAlignment="Stretch" HorizontalContentAlignment="Center" Padding="14" CornerRadius="8" />
        <TextBlock Text="New here? Create an account" FontSize="12" Foreground="#8A75BF" HorizontalAlignment="Center" />
      </StackPanel>
    </Border>
  </Grid>
</UserControl>
""";
    public const string Settings = """
<UserControl xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#F5F6FA">
  <ScrollViewer>
    <StackPanel MaxWidth="620" Margin="28" Spacing="24">
      <TextBlock Text="Make it yours." FontSize="32" FontWeight="SemiBold" Foreground="#242C41" />
      <TextBlock Text="The details that make your workspace feel like home." FontSize="13" Foreground="#8F97A8" TextWrapping="Wrap" />
      <Border Background="White" CornerRadius="16" Padding="28">
        <StackPanel Spacing="18"><TextBlock Text="Profile" FontSize="18" FontWeight="SemiBold" /><TextBox x:Name="DisplayName" Text="Alex Morgan" Watermark="Display name" /><TextBox Text="alex@orbit.design" Watermark="Email address" /><Button Content="Save changes" Background="#8973D0" Foreground="White" CornerRadius="8" /></StackPanel>
      </Border>
      <Border Background="White" CornerRadius="16" Padding="28">
        <StackPanel Spacing="18"><TextBlock Text="Preferences" FontSize="18" FontWeight="SemiBold" /><ToggleSwitch x:Name="Notifications" Content="Desktop notifications" IsChecked="True" /><ToggleSwitch Content="Weekly activity summary" IsChecked="True" /><TextBlock Text="Interface density" FontSize="12" /><Slider x:Name="Density" Minimum="0" Maximum="100" Value="64" /><ComboBox SelectedIndex="0" HorizontalAlignment="Stretch"><ComboBoxItem Content="System appearance" /><ComboBoxItem Content="Light appearance" /><ComboBoxItem Content="Dark appearance" /></ComboBox></StackPanel>
      </Border>
    </StackPanel>
  </ScrollViewer>
</UserControl>
""";
}
