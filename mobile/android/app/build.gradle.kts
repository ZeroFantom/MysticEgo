plugins { id("com.android.application") }
android {
    namespace = "ru.isdarkline.mysticego"
    compileSdk = 36
    defaultConfig {
        applicationId = "ru.isdarkline.mysticego"
        minSdk = 26
        targetSdk = 36
        versionCode = 1
        versionName = "0.1.0-alpha.1"
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    buildTypes {
        debug { applicationIdSuffix = ".debug"; versionNameSuffix = "-debug" }
        release { isMinifyEnabled = false }
    }
}
dependencies {
    implementation("androidx.activity:activity:1.10.1")
    implementation("com.journeyapps:zxing-android-embedded:4.3.0")
    testImplementation("junit:junit:4.13.2")
}
