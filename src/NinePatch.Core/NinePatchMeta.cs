namespace NinePatch.Core;

public readonly record struct NinePatchMeta(
    int Xb,
    int Xe,
    int Yb,
    int Ye,
    int OriginalW,
    int OriginalH,
    int CompressedW,
    int CompressedH,
    int Nx,
    int Ny,
    double ErrorX = 0.0,
    double ErrorY = 0.0,
    double Error2d = 0.0,
    double SavingsPct = 0.0);
