using System.Linq;

namespace RegionKit.Modules.Slideshow;

internal sealed class PlayState
{
	//todo: test ALL OF THIS SHIT
	private const int MAX_INSTANT_INSTRUCTIONS = 1024;
	private readonly Dictionary<Channel, KeyFrame> startKeyFrames;
	private readonly Dictionary<Channel, KeyFrame> endKeyFrames;
	private readonly Dictionary<Channel, KeyFrame> lastKeyFrames = new();
	private readonly Dictionary<Channel, KeyFrame> nextKeyFrames = new();
	private readonly Dictionary<Channel, SetInterpolation> interpolationSettings = new();
	public PlaybackStep CurrentStep => this.owner.playbackSteps[this.CurrentIndex];
	public Frame CurrentFrame => (Frame)this.CurrentStep;
	public int TicksInCurrentFrame => TicksSinceStart - CurrentFrame.index;
	public bool Completed => CurrentIndex >= owner.playbackSteps.Count;
	public int CurrentIndex { get; private set; } = 0;
	public int TicksSinceStart { get; private set; } = 0;
	public string Shader { get; private set; } = "Basic";
	public int DefaultTicksDuration { get; private set; } = 40;
	public readonly Playback owner;

	public PlayState(Playback owner)
	{
		this.owner = owner;
		this.startKeyFrames = CreateDefaultKeyframes(0);
		this.endKeyFrames = CreateDefaultKeyframes(owner.playbackSteps.Count - 1);
		this.interpolationSettings = CreateDefaultInterpolations();
		__logger.LogDebug("pre run updatekeyframes");
		UpdateKeyFrames();
	}
	private static Dictionary<Channel, SetInterpolation> CreateDefaultInterpolations()
	{
		return new() {
			{ Channel.X, new SetInterpolation(InterpolationKind.No, new[] {Channel.X})},
			{ Channel.Y, new SetInterpolation(InterpolationKind.No, new[] {Channel.Y})},
			{ Channel.R, new SetInterpolation(InterpolationKind.No, new[] {Channel.R})},
			{ Channel.G, new SetInterpolation(InterpolationKind.No, new[] {Channel.G})},
			{ Channel.B, new SetInterpolation(InterpolationKind.No, new[] {Channel.B})},
			{ Channel.A, new SetInterpolation(InterpolationKind.No, new[] {Channel.A})},
		};
	}
	private static Dictionary<Channel, KeyFrame> CreateDefaultKeyframes(int at)
	{
		return new() {
				{ Channel.X, new(at, Channel.X, 0f)},
				{ Channel.Y, new(at, Channel.Y, 0f)},
				{ Channel.R, new(at, Channel.R, 1f)},
				{ Channel.G, new(at, Channel.G, 1f)},
				{ Channel.B, new(at, Channel.B, 1f)},
				{ Channel.A, new(at, Channel.A, 1f)},
			};
	}
	public void Update()
	{
		bool overstayed = true;
		for (int i = 0; i < MAX_INSTANT_INSTRUCTIONS; i++)
		{
			bool keepCycling = CurrentStep.InstantlyProgress;
			switch (CurrentStep)
			{
			case SetDelay setDelay:
				DefaultTicksDuration = setDelay.newDelay;
				break;

			case SetInterpolation setInterpol:
				foreach (var channel in setInterpol.channels)
				{
					interpolationSettings[channel] = setInterpol;
				}
				break;
			case SetShader setShader:
				Shader = setShader.shader;
				break;
			case Frame frame:
				if (TicksSinceStart > frame.index + frame.GetTicksDuration(this.DefaultTicksDuration))
				{
					keepCycling = true;
				}
				break;
			}
			if (!keepCycling)
			{
				overstayed = false;
				break;
			}
			CurrentIndex++;
			UpdateKeyFrames();
		}
		if (overstayed)
		{
			throw new NoActualFramesException();
		}
		TicksSinceStart++;
	}
	public void UpdateKeyFrames()
	{
		(string, string) getDebugStuff()
		{
			var prev = this.lastKeyFrames.Select(x => x.ToString()).Stitch();
			var next = this.nextKeyFrames.Select(x => x.ToString()).Stitch();
			return (prev, next);
		}
		__logger.LogDebug($"update keyframes on index {this.CurrentIndex}");
		{
			(var prev, var next) = getDebugStuff();
			__logger.LogDebug($"PRE MODIFY\nprev: {prev}\nnext:{next}");
		}
		lastKeyFrames.Clear();
		nextKeyFrames.Clear();
		for (int i = 0; i <= CurrentIndex; i++)
		{
			if (owner.playbackSteps[i] is not Frame frame) continue;
			foreach (KeyFrame kf in frame.keyFramesHere)
			{
				lastKeyFrames[kf.channel] = kf;
			}
		}
		for (int i = CurrentIndex + 1; i < owner.playbackSteps.Count; i++)
		{
			if (owner.playbackSteps[i] is not Frame frame) continue;
			foreach (KeyFrame kf in frame.keyFramesHere)
			{
				nextKeyFrames[kf.channel] = kf;
			}
		}
		{
			(var prev, var next) = getDebugStuff();
			__logger.LogDebug($"POST MODIFY\nprev: {prev}\nnext:{next}");
		}
		//i hate this lmao
	}
	public SetInterpolation GetInterpolationSetting(Channel channel) => interpolationSettings[channel];
	public KeyFrame GetLastKeyFrame(Channel channel) => lastKeyFrames.TryGetValue(channel, out KeyFrame frame) ? frame : startKeyFrames[channel];
	public KeyFrame GetUpcomingKeyFrame(Channel channel) => nextKeyFrames.TryGetValue(channel, out KeyFrame frame) ? frame : endKeyFrames[channel];
	public int CurrentTransitionFrames(Channel channel) => GetLastKeyFrame(channel).atFrame - GetUpcomingKeyFrame(channel).atFrame;
	public int CurrentTransitionTicks(Channel channel) => CountTickLengths(GetLastKeyFrame(channel).atFrame, GetUpcomingKeyFrame(channel).atFrame);
	private int CountTickLengths(int from, int to)
	{
		int result = 0;
		for (int i = Mathf.Min(from, to); i < Mathf.Max(from, to); i++)
		{
			if (owner.playbackSteps[i] is Frame frame)
			{
				result += frame.GetTicksDuration(DefaultTicksDuration);
			}
		}
		return result;
	}

	//todo: check if lerping is right
	public float GetChannelValue(Channel channel)
	{
		SetInterpolation interpolationSetting = interpolationSettings[channel];
		//a
		KeyFrame lastKeyFrame = GetLastKeyFrame(channel);
		KeyFrame upcomingKeyFrame = GetUpcomingKeyFrame(channel);
		int ticksInTransitionSoFar = CountTickLengths(lastKeyFrame.atFrame, CurrentIndex) + this.TicksInCurrentFrame;
		int ticksInTransitionTotal = CurrentTransitionTicks(channel);
		return interpolationSetting.interpolator(lastKeyFrame.value, upcomingKeyFrame.value, (float)ticksInTransitionSoFar / (float)ticksInTransitionTotal);

	}

	public SlideShowInstant ThisInstant()
	{
		string elementName = CurrentFrame.elementName;
		string shader = Shader;
		Vector2 position = new(GetChannelValue(Channel.X), GetChannelValue(Channel.Y));
		Color color = new(GetChannelValue(Channel.R), GetChannelValue(Channel.G), GetChannelValue(Channel.B), GetChannelValue(Channel.A));
		return new SlideShowInstant(elementName, shader, position, color);
	}
	sealed class NoActualFramesException : Exception
	{
		public override string Message => $"Advanced {MAX_INSTANT_INSTRUCTIONS} times without finding a frame; presuming that there are no actual frames.";
	}
}
