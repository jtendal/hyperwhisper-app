//! First-run mode for the Windows-only application.
//! Local transcription and no post-processing require no hosted account.

pub const HYPER_MODE_ID: &str = "00000000-0000-0000-0000-000000000001";
pub const HYPER_MODE_NAME: &str = "Hyper";
pub const HYPER_MODE_PRESET: &str = "hyper";

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ModeSeed {
    pub id: String,
    pub name: String,
    pub preset: String,
    pub language: String,
    pub provider_type: String,
    pub cloud_provider: String,
    pub cloud_accuracy_tier: String,
    pub cloud_transcription_model: String,
    pub post_processing_mode: i32,
    pub post_processing_provider: String,
    pub cloud_post_processing_model: String,
    pub english_spelling: String,
    pub punctuation: bool,
    pub capitalization: bool,
    pub profanity_filter: bool,
    pub custom_instructions: String,
    pub is_default: bool,
    pub is_system_provided: bool,
    pub sort_order: i32,
}

pub fn mode_seed_default(region: Option<&str>) -> ModeSeed {
    ModeSeed {
        id: HYPER_MODE_ID.to_string(),
        name: HYPER_MODE_NAME.to_string(),
        preset: HYPER_MODE_PRESET.to_string(),
        language: "auto".to_string(),
        provider_type: "local".to_string(),
        cloud_provider: String::new(),
        cloud_accuracy_tier: String::new(),
        cloud_transcription_model: String::new(),
        post_processing_mode: 0,
        post_processing_provider: String::new(),
        cloud_post_processing_model: String::new(),
        english_spelling: hw_text::EnglishSpelling::for_region(region)
            .raw_value()
            .to_string(),
        punctuation: true,
        capitalization: true,
        profanity_filter: false,
        custom_instructions: String::new(),
        is_default: true,
        is_system_provided: true,
        sort_order: 0,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn fresh_install_has_no_hosted_dependencies() {
        let seed = mode_seed_default(Some("US"));
        assert_eq!(seed.provider_type, "local");
        assert!(seed.cloud_provider.is_empty());
        assert!(seed.cloud_transcription_model.is_empty());
        assert_eq!(seed.post_processing_mode, 0);
        assert!(seed.post_processing_provider.is_empty());
        assert!(seed.cloud_post_processing_model.is_empty());
        assert!(seed.is_default && seed.is_system_provided);
        assert_eq!(seed.id, HYPER_MODE_ID);
    }

    #[test]
    fn region_changes_only_spelling() {
        let us = mode_seed_default(Some("US"));
        for region in ["GB", "AU", "CA", "JP", "ZZ", ""] {
            let mut seed = mode_seed_default(Some(region));
            seed.english_spelling = us.english_spelling.clone();
            assert_eq!(seed, us);
        }
        assert_eq!(mode_seed_default(Some("GB")).english_spelling, "british");
    }
}
