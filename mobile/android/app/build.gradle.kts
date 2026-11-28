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
dependencies { testImplementation("junit:junit:4.13.2") }
