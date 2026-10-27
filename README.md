# Multi-valued_MP_AHP_Method

**This program computes the multi-valued morphological profile using the AHP (Analytic Hierarchy Process) vector ordering algorithm.**

## The method

The pixel-vectors covered by the structuring element are treated as the *alternatives* of a multi-criteria decision problem, and the image bands as its *criteria*. For each band, AHP builds a pairwise comparison matrix between all the pixel-vectors of the neighborhood. Instead of Saaty's subjective 1-9 scale, an objective preference function proportional to the radiometric difference is used: P(a_i, a_j) = 1 + (g(a_i) - g(a_j)) if g(a_i) >= g(a_j), and 1 / (1 + (g(a_j) - g(a_i))) otherwise.

Each matrix is normalized by its column sums, the local priority of each pixel-vector is the mean of its row, and the global priority S(a_i) is the weighted sum of the local priorities using the band weights. A single preference function is used: the **supremum** (dilation) is the pixel-vector with the highest global priority and the **infimum** (erosion) the one with the lowest. Ties are broken deterministically so that both extrema are always unique.

The supremum and infimum obtained in every neighborhood are used to compute, for each size of the structuring element: erosion, dilation, opening, closing, opening by reconstruction and closing by reconstruction (multivariate morphological profile).

## Implementation

- **Language:** C# (WPF desktop application with a graphical interface)
- **Framework:** .NET 10 (`net10.0-windows`)
- **Development environment:** Visual Studio Code with the *C# Dev Kit* extension
- **Operating system:** Windows 64-bit (WPF is Windows-only)
- **Structuring element:** Euclidean disk of radius i (pixels with dx² + dy² ≤ i²: 5, 13, 29, 49 and 81 pixels for i = 1 to 5), used in a single pass: all the pixel-vectors covered by the disk are ranked together. Border pixels whose disk falls outside the image are kept unchanged. The geodesic reconstruction uses the elementary disk of radius 1 (5 pixels).
- **Reconstruction (version 4):** the pointwise infimum and supremum between two pixel-vectors in the geodesic reconstruction no longer compare the two vectors alone, which is not transitive and can produce Condorcet cycles (A > B, B > C and C > A). Each pixel-vector now receives the method's own score computed against a fixed reference set of pixel-vectors sampled from the input image (see version 5 below); equal scores are broken by the lexicographic order of the bands. This gives a fixed total order, and the marker sequence is kept monotone, so the reconstruction always stops on an exact fixed point (use `0` iterations and `100` % for the exact reconstruction). The ranking of the pixel-vectors of the neighborhood is unchanged. The added code is marked `[V4-ANTI-CONDORCET]` in `MainWindow.xaml.cs`.
- **Reference set (version 5):** the score of each pixel-vector used for this fixed order is now computed against ALL the pixels of the image (exact computation, band by band, from the band histograms), instead of a sample of 256 pixel-vectors. The added code is marked `[V5]` in `MainWindow.xaml.cs`.
- **Interface language:** French

## Repository content

```
EMP_AHP_UniquePreferenceFunction.sln              Visual Studio / VS Code solution
global.json          .NET SDK version (10.0.x)
.vscode/             VS Code build, run (F5) and publish tasks
EMP_AHP_UniquePreferenceFunction/
    MainWindow.xaml.cs   ordering algorithm and morphological operators
    MainWindow.xaml      graphical interface
    App.xaml(.cs)        application entry point
    EMP_AHP_UniquePreferenceFunction.csproj
```

## How to run

### Option 1 — Standalone executable (no installation)

Download `EMP_AHP_UniquePreferenceFunction.exe` from the [**Releases**](https://github.com/slhaddad/Multi_valued_MP_AHP_Method/releases/latest) page of this repository (latest release, `v5`) and double-click it. The results are written to the `IMAGES-résultat` folder created next to the executable (see *Outputs* below). It is self-contained: neither .NET nor VS Code is required. You can also build it yourself with Option 3.

### Option 2 — Visual Studio Code

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download) and the **C# Dev Kit** extension for VS Code.
2. Clone or download this repository, then in VS Code: *File > Open Folder* and select the repository folder.
3. Press **F5** to build and run the program.

### Option 3 — Command line

```powershell
dotnet build "EMP_AHP_UniquePreferenceFunction\EMP_AHP_UniquePreferenceFunction.csproj"
dotnet run --project "EMP_AHP_UniquePreferenceFunction\EMP_AHP_UniquePreferenceFunction.csproj"

# Create the standalone executable in the Executable\ folder
dotnet publish "EMP_AHP_UniquePreferenceFunction\EMP_AHP_UniquePreferenceFunction.csproj" -c Release -o Executable
```

## Input parameters

| Field in the interface (French) | Meaning |
|---|---|
| *Donnez la taille maximale de l'ES : i =* | Maximum size of the disk-shaped structuring element. The program computes all sizes from 1 to i. |
| *Selection bande — Parcourir* | Select the image bands (one grayscale image per band, PNG/JPEG/BMP/TIFF, all of the same size). Multiple selection is allowed. |
| *Donnez le nombre d'itérations pour la stabilité* | Maximum number of iterations of the geodesic reconstruction (`0` = unlimited, run until stability). |
| *Donnez le pourcentage de ressemblance (0 - 100)* | Stability threshold: the reconstruction stops when two successive images are identical on at least this percentage of pixels (e.g. `35`). |
| *Donnez les poids des différentes bandes* | Band weights, separated by semicolons, no spaces (e.g. `0,45;0,35;0,20`). One weight per band. |

Then click **Executer**.

## Outputs

All results are written to the **`IMAGES-résultat`** folder, created next to the solution (`.sln`) file, or next to the executable if it is run alone:

- one TIFF image per band and per structuring-element size for each operator: `Erod`, `Dilat`, `OuvertureStandard`, `FermetureStandard`, `OuvertReconstruction`, `FermeReconstruction` (named `<operator> B_<band> ES_<size>.tiff`);
- three multiband images in **ENVI** format (`.img` + `.hdr`):
  - `OuvertReconstruction-multibande`: all openings by reconstruction;
  - `FermeReconstruction-multibande`: all closings by reconstruction;
  - `EXTENDED-PROFIL-MOR-multibande`: the extended morphological profile (openings and closings by reconstruction together).

## Multivariate Mathematical Morphology through Vector Ordering

The code published in this repository implements new algorithms that extend mathematical morphology operators to multivalued images, particularly multispectral satellite images. These approaches also apply to RGB color images.

This extension relies on vector-ordering strategies applied to the pixel-vectors within the neighborhood defined by a structuring element (SE). For each neighborhood, the ordering uniquely identifies the infimum and supremum pixel-vectors required by the fundamental erosion and dilation operations.

The proposed strategies are based on:

- The comparative structure of multi-criteria decision analysis methods: AHP, PROMETHEE (usual, U-shape, level and Gaussian preference functions), QUALIFLEX and TOPSIS (p = 1 and p = 2);
- Numeral-system-based methods;
- Outranking relations between pixel-vectors;
- Cumulative distances: SID (Spectral Information Divergence) and SAD (Spectral Angle Distance);
- The conventional lexicographic order.

Each program computes multivariate erosion, dilation, opening, closing, opening by reconstruction and closing by reconstruction, using a disk-shaped structuring element of increasing size. It also exports the extended morphological profile in ENVI format.

The methods are described in detail in the following works:

- Samir, L. H., Akila, K., & Aude Nuscia, T. (2025). New Vector Ordering Algorithms for Multivalued Mathematical Morphology Computing Based on Multicriteria Decision Making Systems: L'haddad et al. *Computational and Applied Mathematics*, 44(6), 320.
- L'haddad, S., Kemmouche, A., & Taïbi, A. N. (2024). Computing Multivalued Mathematical Morphology on Multiband Images Using Algorithms for Multicriteria Analysis. *Image Analysis and Stereology*, 43(1), 23-40.
- L'haddad, S., & Kemmouche, A. (2021, January). New Approach for Multi-valued Mathematical Morphology Computation. In *International Conference on Artificial Intelligence and its Applications* (pp. 514-523). Cham: Springer International Publishing.
- L'haddad, S., & Kemmouche, A. (2020, December). Vector Ordering Algorithms for Morphological Multi-Valued Operators Using Improved F-score Technique and Numeral Systems. In *2020 4th International Symposium on Informatics and its Applications (ISIA)* (pp. 1-6). IEEE.
- Plaza, A., Martínez, P., Plaza, J., & Pérez, R. (2005). Dimensionality reduction and classification of hyperspectral image data using sequences of extended morphological transformations. *IEEE Transactions on Geoscience and Remote Sensing*, 43(3), 466-479.

A more detailed description of these methods will be provided in my forthcoming PhD thesis (in French), to be published online and on my ResearchGate profile (Samir L'Haddad, USTHB, Algiers, Algeria).
