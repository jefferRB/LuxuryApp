namespace LuxuryApp.Services.PublicImages
{
    public sealed class PublicImageCropRequest
    {
        public int? CropX { get; set; }

        public int? CropY { get; set; }

        public int? CropWidth { get; set; }

        public int? CropHeight { get; set; }

        public bool HasCrop =>
            CropX.HasValue &&
            CropY.HasValue &&
            CropWidth.HasValue &&
            CropHeight.HasValue;
    }
}
