#!/usr/bin/env python3
import os

from mlagents import torch_utils
from mlagents.plugins.stats_writer import register_stats_writer_plugins
from mlagents.trainers import learn
from mlagents.trainers.directory_utils import validate_existing_directories
from mlagents.trainers.environment_parameter_manager import EnvironmentParameterManager
from mlagents.trainers.env_manager import EnvironmentStep
from mlagents.trainers.simple_env_manager import SimpleEnvManager
from mlagents.trainers.stats import StatsReporter
from mlagents.trainers.trainer import TrainerFactory
from mlagents.trainers.trainer_controller import TrainerController
from mlagents.trainers.training_status import GlobalTrainingStatus
from mlagents_envs.side_channel.engine_configuration_channel import (
    EngineConfig,
    EngineConfigurationChannel,
)
from mlagents_envs.side_channel.environment_parameters_channel import (
    EnvironmentParametersChannel,
)
from mlagents_envs.side_channel.stats_side_channel import StatsSideChannel
from mlagents_envs.timers import hierarchical_timer


class EditorSimpleEnvManager(SimpleEnvManager):
    def _step(self):
        all_action_info = self._take_step(self.previous_step)
        self.previous_all_action_info = all_action_info

        for brain_name, action_info in all_action_info.items():
            if len(action_info.agent_ids) > 0:
                self.env.set_actions(brain_name, action_info.env_action)
        self.env.step()
        all_step_result = self._generate_all_results()

        step_info = EnvironmentStep(
            all_step_result, 0, self.previous_all_action_info, {}
        )
        self.previous_step = step_info
        return [step_info]


def run_training_single_process(run_seed, options) -> None:
    with hierarchical_timer("run_training.setup"):
        if options.env_settings.num_envs != 1:
            raise ValueError(
                "Editor single-process training only supports --num-envs=1."
            )

        torch_utils.set_torch_config(options.torch_settings)
        checkpoint_settings = options.checkpoint_settings
        env_settings = options.env_settings
        engine_settings = options.engine_settings

        run_logs_dir = checkpoint_settings.run_logs_dir
        port = None if env_settings.env_path is None else env_settings.base_port

        validate_existing_directories(
            checkpoint_settings.write_path,
            checkpoint_settings.resume,
            checkpoint_settings.force,
            checkpoint_settings.maybe_init_path,
        )
        os.makedirs(run_logs_dir, exist_ok=True)

        if checkpoint_settings.resume:
            GlobalTrainingStatus.load_state(
                os.path.join(run_logs_dir, "training_status.json")
            )

        for stats_writer in register_stats_writer_plugins(options):
            StatsReporter.add_writer(stats_writer)

        env_factory = learn.create_environment_factory(
            env_settings.env_path,
            engine_settings.no_graphics,
            run_seed,
            port,
            env_settings.env_args,
            os.path.abspath(run_logs_dir),
        )

        env_parameters = EnvironmentParametersChannel()
        engine_configuration_channel = EngineConfigurationChannel()
        engine_configuration_channel.set_configuration(
            EngineConfig(
                width=engine_settings.width,
                height=engine_settings.height,
                quality_level=engine_settings.quality_level,
                time_scale=engine_settings.time_scale,
                target_frame_rate=engine_settings.target_frame_rate,
                capture_frame_rate=engine_settings.capture_frame_rate,
            )
        )
        stats_channel = StatsSideChannel()

        env = env_factory(
            0, [env_parameters, engine_configuration_channel, stats_channel]
        )
        env_manager = EditorSimpleEnvManager(env, env_parameters)
        env_parameter_manager = EnvironmentParameterManager(
            options.environment_parameters,
            run_seed,
            restore=checkpoint_settings.resume,
        )

        trainer_factory = TrainerFactory(
            trainer_config=options.behaviors,
            output_path=checkpoint_settings.write_path,
            train_model=not checkpoint_settings.inference,
            load_model=checkpoint_settings.resume,
            seed=run_seed,
            param_manager=env_parameter_manager,
            init_path=checkpoint_settings.maybe_init_path,
            multi_gpu=False,
        )
        trainer_controller = TrainerController(
            trainer_factory,
            checkpoint_settings.write_path,
            checkpoint_settings.run_id,
            env_parameter_manager,
            not checkpoint_settings.inference,
            run_seed,
        )

    try:
        trainer_controller.start_learning(env_manager)
    finally:
        env_manager.close()
        learn.write_run_options(checkpoint_settings.write_path, options)
        learn.write_timing_tree(run_logs_dir)
        learn.write_training_status(run_logs_dir)


if __name__ == "__main__":
    learn.run_training = run_training_single_process
    learn.main()
