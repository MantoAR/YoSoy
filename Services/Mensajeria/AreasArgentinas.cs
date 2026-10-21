// Los indicativos interurbanos (códigos de área) de Argentina, tal como los publica ENACOM.
//
// Fuente: https://www.enacom.gob.ar/indicativos-interurbanos_p143 (planilla "AREAS LOCALES 300", archivo del 21/05/2024), descargada el 2026-10-21: 300 códigos,
// uno de 2 dígitos (11), 38 de 3 y 261 de 4. Son datos públicos (no hay nada de nadie acá). Para actualizarla: bajar la planilla de esa página, sacar la primera columna
// y regenerar estas dos listas; la prueba AreasArgentinasTests cuenta 1 + 38 + 261 y vigila que ningún código quede repetido.
//
// Se usa solo para dos cosas: separar el código de área del abonado al mostrar un teléfono (TelefonoWhatsApp) y saber si un número de 10 dígitos PUEDE ser argentino
// (si su código de área no figura en la lista, no lo es). Un código de 3 dígitos y uno de 4 pueden empezar igual (343 y 3435): gana el más largo (AreasArgentinasTests lo fija con 3435).
public static class AreasArgentinas
{
    private static readonly HashSet<string> TresDigitos = new(
        @"
        220 221 223 230 236 237 249 260 261 263 264 266 280 291 294 297 298 299 336 341 342 343 345
        348 351 353 358 362 364 370 376 379 380 381 383 385 387 388
".Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static readonly HashSet<string> CuatroDigitos = new(
        @"
        2202 2221 2223 2224 2225 2226 2227 2229 2241 2242 2243 2244 2245 2246 2252 2254 2255 2257
        2261 2262 2264 2265 2266 2267 2268 2271 2272 2273 2274 2281 2283 2284 2285 2286 2291 2292
        2296 2297 2302 2314 2316 2317 2320 2323 2324 2325 2326 2331 2333 2334 2335 2336 2337 2338
        2342 2343 2344 2345 2346 2352 2353 2354 2355 2356 2357 2358 2392 2393 2394 2395 2396 2473
        2474 2475 2477 2478 2622 2624 2625 2626 2646 2647 2648 2651 2655 2656 2657 2658 2901 2902
        2903 2920 2921 2922 2923 2924 2925 2926 2927 2928 2929 2931 2932 2933 2934 2935 2936 2940
        2942 2945 2946 2948 2952 2953 2954 2962 2963 2964 2966 2972 2982 2983 3327 3329 3382 3385
        3387 3388 3400 3401 3402 3404 3405 3406 3407 3408 3409 3435 3436 3437 3438 3442 3444 3445
        3446 3447 3454 3455 3456 3458 3460 3462 3463 3464 3465 3466 3467 3468 3469 3471 3472 3476
        3482 3483 3487 3489 3491 3492 3493 3496 3497 3498 3521 3522 3524 3525 3532 3533 3537 3541
        3542 3543 3544 3546 3547 3548 3549 3562 3563 3564 3571 3572 3573 3574 3575 3576 3582 3583
        3584 3585 3711 3715 3716 3718 3721 3725 3731 3734 3735 3741 3743 3751 3754 3755 3756 3757
        3758 3772 3773 3774 3775 3777 3781 3782 3786 3821 3825 3826 3827 3832 3835 3837 3838 3841
        3843 3844 3845 3846 3854 3855 3856 3857 3858 3861 3862 3863 3865 3867 3868 3869 3873 3876
        3877 3878 3885 3886 3887 3888 3891 3892 3894
".Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public const string BuenosAires = "11";

    public static int CantidadDeTresDigitos => TresDigitos.Count;
    public static int CantidadDeCuatroDigitos => CuatroDigitos.Count;

    /// <summary>
    /// Cuántos dígitos tiene el código de área al principio de <paramref name="nacional"/> (el número de 10 dígitos): 4, 3 o 2, buscando primero el más largo, o null si
    /// no empieza con ningún código de área argentino.
    /// </summary>
    public static int? LargoDelCodigo(string nacional)
    {
        if (nacional.Length < 10) return null;
        if (CuatroDigitos.Contains(nacional[..4])) return 4;
        if (TresDigitos.Contains(nacional[..3])) return 3;
        if (nacional.StartsWith(BuenosAires, StringComparison.Ordinal)) return 2;
        return null;
    }
}
